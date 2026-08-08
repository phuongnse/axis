import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { addServiceIdentityKey, createServiceIdentity } from '../api';
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
      <ServiceIdentitiesPage />
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
  });

  it('creates a service identity and refreshes canonical state', async () => {
    const user = userEvent.setup();
    const queryClient = renderPage();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    await screen.findByRole('heading', { name: 'worker-one' });
    await user.type(screen.getByRole('textbox', { name: 'Client identifier' }), ' worker-two ');
    await user.click(screen.getByRole('button', { name: 'Create service identity' }));
    await waitFor(() =>
      expect(createServiceIdentity).toHaveBeenCalledWith(
        { clientId: 'worker-two' },
        expect.anything(),
      ),
    );
    expect(await screen.findByText('Service identity created')).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['service-identities'] });
  });

  it('clears private JWK material before rendering or mutation', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole('heading', { name: 'worker-one' });
    const jwk = screen.getByRole('textbox', { name: 'Public ES256 JWK' });
    await user.click(jwk);
    await user.paste(
      '{"kty":"EC","crv":"P-256","kid":"kid-1","x":"x","y":"y","d":"private-value"}',
    );
    expect(jwk).toHaveValue('');
    expect(
      screen.getByText('Private or symmetric key material is not accepted.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('private-value')).not.toBeInTheDocument();
    expect(addServiceIdentityKey).not.toHaveBeenCalled();
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
