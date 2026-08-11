import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ManagedWindowHost } from '@/components/shared/ManagedWindowHost';
import { ManagedWindowProvider } from '@/components/shared/ManagedWindowManager';
import { axisStyles } from '@/theme.generated';
import { addServiceIdentityKey, createServiceIdentity, revokeServiceIdentityKey } from '../api';
import { serviceIdentitiesManagedWindowRenderers } from '../managed-windows';
import { ServiceIdentitiesPage } from './ServiceIdentitiesPage';

const api = vi.hoisted(() => ({
  list: vi.fn(),
  create: vi.fn(),
  addKey: vi.fn(),
  revokeKey: vi.fn(),
  revoke: vi.fn(),
}));

vi.mock('../api', () => ({
  serviceIdentityQueryKeys: { all: ['service-identities'] },
  serviceIdentitiesQueryOptions: () => ({
    queryKey: ['service-identities', 'list'],
    queryFn: api.list,
  }),
  createServiceIdentity: api.create,
  addServiceIdentityKey: api.addKey,
  revokeServiceIdentityKey: api.revokeKey,
  revokeServiceIdentity: api.revoke,
}));

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <QueryClientProvider client={queryClient}>
      <ManagedWindowProvider renderers={serviceIdentitiesManagedWindowRenderers}>
        <div className="relative h-dvh w-dvw">
          <ServiceIdentitiesPage />
          <ManagedWindowHost />
        </div>
      </ManagedWindowProvider>
    </QueryClientProvider>,
  );
  return queryClient;
}

describe('ServiceIdentitiesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.list.mockResolvedValue([identity()]);
    api.create.mockResolvedValue(identity({ id: 'service-2', clientId: 'worker-two' }));
    api.addKey.mockResolvedValue(
      identity({
        revision: 3,
        keys: [{ id: 'key-1', kid: 'kid-1', thumbprint: 'thumb', status: 'Active' }],
      }),
    );
    api.revokeKey.mockResolvedValue(
      identity({
        revision: 4,
        keys: [{ id: 'key-1', kid: 'kid-1', thumbprint: 'thumb', status: 'Revoked' }],
      }),
    );
    api.revoke.mockResolvedValue(identity({ status: 'Revoked', revision: 3 }));
  });

  it('composes the shared resource workspace and creates from a managed window', async () => {
    const user = userEvent.setup();
    const queryClient = renderPage();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const page = document.querySelector<HTMLElement>('[data-slot="page-layout"]');
    const workspace = document.querySelector<HTMLElement>('[data-slot="resource-workspace"]');
    const content = document.querySelector<HTMLElement>('[data-slot="resource-workspace-content"]');
    const table = await screen.findByRole('region', { name: 'Service identities' });
    const create = await within(table).findByRole('button', { name: 'Create service identity' });

    expect(page).toHaveAttribute('data-scroll-mode', 'contained');
    expect(page).toContainElement(workspace);
    expect(workspace?.querySelectorAll('[data-slot="page-header"]')).toHaveLength(1);
    expect(content).toContainElement(table);
    expect(content?.querySelectorAll('[data-slot="data-table"]')).toHaveLength(1);
    expect(within(table).queryByRole('columnheader', { name: 'Actions' })).not.toBeInTheDocument();
    expect(create).toHaveClass(
      axisStyles.density.minHeight.touchTarget,
      axisStyles.density.minWidth.touchTarget,
      axisStyles.density.minHeight.compactControlAtSmall,
      axisStyles.density.minWidth.compactControlAtSmall,
    );

    await user.click(create);
    const dialog = await screen.findByRole('dialog', { name: 'Create service identity' });
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Client identifier' }),
      ' worker-two ',
    );
    await user.click(within(dialog).getByRole('button', { name: 'Create service identity' }));

    await waitFor(() =>
      expect(createServiceIdentity).toHaveBeenCalledWith(
        { clientId: 'worker-two' },
        expect.anything(),
      ),
    );
    expect(await within(dialog).findByText('Service identity created')).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['service-identities'] });
  });

  it('clears private JWK material before rendering or mutation', async () => {
    const user = userEvent.setup();
    renderPage();
    const table = await screen.findByRole('region', { name: 'Service identities' });
    await user.click(await within(table).findByRole('button', { name: 'worker-one' }));
    const dialog = await screen.findByRole('dialog', { name: 'worker-one' });
    const jwk = within(dialog).getByRole('textbox', { name: 'Public ES256 JWK' });
    await user.click(jwk);
    await user.paste(
      '{"kty":"EC","crv":"P-256","kid":"kid-1","x":"x","y":"y","d":"private-value"}',
    );

    expect(jwk).toHaveValue('');
    expect(
      within(dialog).getByText('Private or symmetric key material is not accepted.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('private-value')).not.toBeInTheDocument();
    expect(addServiceIdentityKey).not.toHaveBeenCalled();
  });

  it('uses each canonical revision for the next public-key mutation', async () => {
    const user = userEvent.setup();
    renderPage();
    const table = await screen.findByRole('region', { name: 'Service identities' });
    await user.click(await within(table).findByRole('button', { name: 'worker-one' }));
    const dialog = await screen.findByRole('dialog', { name: 'worker-one' });
    const publicJwk = JSON.stringify({
      kty: 'EC',
      crv: 'P-256',
      kid: 'kid-1',
      x: 'public-x',
      y: 'public-y',
    });
    const jwk = within(dialog).getByRole('textbox', { name: 'Public ES256 JWK' });
    await user.click(jwk);
    await user.paste(publicJwk);
    await user.click(within(dialog).getByRole('button', { name: 'Add public key' }));

    await waitFor(() =>
      expect(addServiceIdentityKey).toHaveBeenCalledWith('service-1', {
        expectedRevision: 2,
        publicJwk,
      }),
    );
    expect(await within(dialog).findByText('Public key added')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Revoke key' }));
    const confirmation = await screen.findByRole('alertdialog', {
      name: 'Revoke this public key?',
    });
    await user.click(within(confirmation).getByRole('button', { name: 'Revoke key' }));

    await waitFor(() =>
      expect(revokeServiceIdentityKey).toHaveBeenCalledWith('service-1', 'key-1', 3),
    );
  });

  it('preserves a public-key draft through minimize and guards destructive closure', async () => {
    const user = userEvent.setup();
    renderPage();
    const table = await screen.findByRole('region', { name: 'Service identities' });
    await user.click(await within(table).findByRole('button', { name: 'worker-one' }));
    const dialog = await screen.findByRole('dialog', { name: 'worker-one' });
    const jwk = within(dialog).getByRole('textbox', { name: 'Public ES256 JWK' });
    await user.click(jwk);
    await user.paste('{"kty":"EC"');
    await user.click(within(dialog).getByRole('button', { name: 'Minimize dialog' }));

    const dock = document.querySelector<HTMLElement>('[data-slot="managed-window-dock"]');
    await user.click(within(dock as HTMLElement).getByRole('button', { name: 'Restore dialog' }));
    expect(jwk).toHaveValue('{"kty":"EC"');

    await user.click(within(dialog).getByRole('button', { name: /^Close$/ }));
    const confirmation = await screen.findByRole('alertdialog', {
      name: 'Discard this public-key draft?',
    });
    await user.click(within(confirmation).getByRole('button', { name: 'Keep editing' }));
    expect(dialog).toBeVisible();

    await user.click(within(dialog).getByRole('button', { name: /^Close$/ }));
    await user.click(
      within(
        await screen.findByRole('alertdialog', { name: 'Discard this public-key draft?' }),
      ).getByRole('button', { name: 'Discard public-key draft' }),
    );
    await waitFor(() => expect(dialog).not.toBeInTheDocument());
  });

  it('fails closed on query errors and recovers to the explicit empty state', async () => {
    const user = userEvent.setup();
    api.list.mockRejectedValueOnce(new TypeError('network unavailable'));
    renderPage();
    const table = await screen.findByRole('region', { name: 'Service identities' });

    expect(await within(table).findByText('Unable to load service identities')).toBeVisible();
    expect(
      within(table).queryByRole('button', { name: 'Create service identity' }),
    ).not.toBeInTheDocument();
    api.list.mockResolvedValue([]);
    await user.click(within(table).getByRole('button', { name: 'Retry' }));

    expect(
      await within(table).findByText('No service identities in this Workspace.'),
    ).toBeVisible();
    expect(within(table).getByRole('button', { name: 'Create service identity' })).toBeVisible();
  });
});

function identity(overrides: Record<string, unknown> = {}) {
  return {
    id: 'service-1',
    clientId: 'worker-one',
    workspaceId: 'workspace-1',
    status: 'Active',
    workspaceGrantStatus: 'Active',
    revision: 2,
    subject: { kind: 'Service', subjectId: 'service-1' },
    keys: [],
    ...overrides,
  };
}
