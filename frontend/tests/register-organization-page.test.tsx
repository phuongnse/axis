import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { RegisterOrganizationPage } from '@/features/auth/components/RegisterOrganizationPage';
import { renderWithRouter } from './render-with-router';

const navigateMock = vi.fn();

vi.mock('@tanstack/react-router', async () => {
  const actual =
    await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router');
  return {
    ...actual,
    useNavigate: () => navigateMock,
  };
});

const LEGAL_VERSIONS = {
  termsVersion: '2026-05-01',
  privacyVersion: '2026-05-01',
};

function legalVersionsResponse(): Response {
  return {
    ok: true,
    status: 200,
    text: () => Promise.resolve(JSON.stringify(LEGAL_VERSIONS)),
  } as unknown as Response;
}

function slugPreviewResponse(slug = 'o-brien-co'): Response {
  return {
    ok: true,
    status: 200,
    text: () => Promise.resolve(JSON.stringify({ slug })),
  } as unknown as Response;
}

async function fillOrganizationForm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Organization name'), "O'Brien & Co.");
  await user.type(screen.getByLabelText('Organization contact email'), 'admin@company.com');
  await user.click(screen.getByRole('checkbox', { name: /terms of service/i }));
}

describe('RegisterOrganizationPage', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    navigateMock.mockReset();
    sessionStorage.clear();
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/legal/versions')) return Promise.resolve(legalVersionsResponse());
      if (url.includes('/api/organizations/slug-preview')) {
        return Promise.resolve(slugPreviewResponse());
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('shows inline validation errors when form is empty', async () => {
    const user = userEvent.setup();
    await renderWithRouter(<RegisterOrganizationPage />, { path: '/register/organization' });

    await user.click(screen.getByRole('button', { name: /register organization/i }));

    expect(screen.getByText('Organization name is required')).toBeInTheDocument();
    expect(screen.getByText('Organization contact email is required')).toBeInTheDocument();
    expect(
      screen.getByText('You must accept the Terms of Service and Privacy Policy'),
    ).toBeInTheDocument();
  });

  it('shows slug preview and submits the organization registration payload', async () => {
    const user = userEvent.setup();
    let registerBody: Record<string, unknown> | undefined;
    let registerHeaders: Record<string, string> | undefined;

    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/legal/versions')) return Promise.resolve(legalVersionsResponse());
      if (url.includes('/api/organizations/slug-preview')) {
        return Promise.resolve(slugPreviewResponse());
      }
      if (url.includes('/api/organizations') && init?.method === 'POST') {
        registerBody = JSON.parse(String(init.body)) as Record<string, unknown>;
        registerHeaders = init.headers as Record<string, string>;
        return Promise.resolve({
          ok: true,
          status: 200,
          text: () =>
            Promise.resolve(
              JSON.stringify({
                message:
                  'Registration successful. Please check your email to verify your organization.',
              }),
            ),
        } as unknown as Response);
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<RegisterOrganizationPage />, { path: '/register/organization' });

    await fillOrganizationForm(user);

    expect(await screen.findByText('o-brien-co.axis.app')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /register organization/i }));

    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith({ to: '/register/confirmation' }),
    );
    expect(registerBody).toEqual({
      orgName: "O'Brien & Co.",
      organizationContactEmail: 'admin@company.com',
      acceptedTermsVersion: '2026-05-01',
      acceptedPrivacyVersion: '2026-05-01',
      subscriptionPlanId: null,
    });
    expect(registerHeaders?.['Idempotency-Key']).toBeTruthy();
    const stored = sessionStorage.getItem('axis.registration-context');
    expect(stored).toContain('admin@company.com');
    expect(stored).toContain("O'Brien & Co.");
  });

  it('maps backend validation errors to inline organization fields', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/legal/versions')) return Promise.resolve(legalVersionsResponse());
      if (url.includes('/api/organizations/slug-preview')) {
        return Promise.resolve(slugPreviewResponse());
      }
      if (url.includes('/api/organizations') && init?.method === 'POST') {
        return Promise.resolve({
          ok: false,
          status: 400,
          statusText: 'Bad Request',
          json: () =>
            Promise.resolve({
              errors: {
                orgName: ['Organization name must be at least 2 characters.'],
                organizationContactEmail: ['Enter a valid organization email.'],
              },
            }),
        } as unknown as Response);
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<RegisterOrganizationPage />, { path: '/register/organization' });

    await fillOrganizationForm(user);
    await user.click(screen.getByRole('button', { name: /register organization/i }));

    expect(
      await screen.findByText('Organization name must be at least 2 characters.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Enter a valid organization email.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /register organization/i })).toBeEnabled();
  });

  it('shows generic server error when organization registration fails', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/legal/versions')) return Promise.resolve(legalVersionsResponse());
      if (url.includes('/api/organizations/slug-preview')) {
        return Promise.resolve(slugPreviewResponse());
      }
      if (url.includes('/api/organizations') && init?.method === 'POST') {
        return Promise.resolve({
          ok: false,
          status: 500,
          statusText: 'Internal Server Error',
          json: () => Promise.resolve({ message: 'boom' }),
        } as unknown as Response);
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<RegisterOrganizationPage />, { path: '/register/organization' });

    await fillOrganizationForm(user);
    await user.click(screen.getByRole('button', { name: /register organization/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Something went wrong, please try again',
    );
    expect(screen.getByRole('button', { name: /register organization/i })).toBeEnabled();
  });
});
