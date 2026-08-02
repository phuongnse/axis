import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ManagedWindowHost } from '@/components/shared/ManagedWindowHost';
import {
  ManagedWindowProvider,
  useManagedWindowActions,
} from '@/components/shared/ManagedWindowManager';
import type {
  BusinessObjectRecordDetail,
  BusinessObjectRecordRuleEvaluation,
} from '@/features/applications';
import {
  applicationQueryKeys,
  applicationRecordWindowDescriptor,
  applicationsManagedWindowRenderers,
} from '@/features/applications';
import { managedWindowRenderers } from '@/lib/managed-window-registry';

const recordId = '11111111-1111-4111-8111-111111111111';
const bindingId = '22222222-2222-4222-8222-222222222222';

describe('application workflow UI', () => {
  beforeEach(() => vi.stubGlobal('fetch', vi.fn()));

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('saves a draft, submits it, and makes the submitted record read-only', async () => {
    const user = userEvent.setup();
    const draft = applicationRecord({
      values: {},
      revision: 1,
      status: 'Draft',
    });
    const saved = applicationRecord({
      values: {
        applicant_name: ['Phuong Nguyen'],
        contact_email: ['phuong@example.com'],
        requested_amount: ['12000'],
        purpose: ['Working capital'],
      },
      revision: 2,
      status: 'Draft',
    });
    const submitted = applicationRecord({
      ...saved,
      revision: 3,
      status: 'Submitted',
      ruleEvaluations: [matchedEvaluation()],
      submittedAt: '2026-08-02T10:00:00Z',
    });
    const requests: Array<{ method: string; path: string; body?: unknown }> = [];

    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const path = requestPath(input);
      const method = init?.method ?? 'GET';
      if (path === `/api/business-object-records/${recordId}` && method === 'GET') {
        return jsonResponse(draft);
      }
      if (path === `/api/business-object-records/${recordId}` && method === 'PUT') {
        const body = JSON.parse(String(init?.body));
        requests.push({ method, path, body });
        return jsonResponse(saved);
      }
      if (path === `/api/business-object-records/${recordId}/submit` && method === 'POST') {
        const body = JSON.parse(String(init?.body));
        requests.push({ method, path, body });
        return jsonResponse({
          isSubmitted: true,
          record: submitted,
          ruleEvaluations: submitted.ruleEvaluations,
        });
      }
      throw new Error(`Unexpected ${method} ${path}`);
    });

    const queryClient = renderWorkflowWindow();

    await waitFor(() => {
      expect(queryClient.getQueryState(applicationQueryKeys.detail(recordId))?.status).toBe(
        'success',
      );
    });
    expect(await screen.findByLabelText(/Applicant name/)).toHaveValue('');
    await user.type(screen.getByLabelText(/Applicant name/), 'Phuong Nguyen');
    await user.type(screen.getByLabelText('Contact email'), 'phuong@example.com');
    await user.type(screen.getByLabelText('Requested amount'), '12000');
    await user.type(screen.getByLabelText('Purpose'), 'Working capital');

    await user.click(screen.getByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]).toMatchObject({
      method: 'PUT',
      body: {
        expectedRevision: 1,
        values: {
          applicant_name: ['Phuong Nguyen'],
          contact_email: ['phuong@example.com'],
          requested_amount: ['12000'],
          purpose: ['Working capital'],
        },
      },
    });

    expect(await screen.findByText('Revision 2')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Submit application' }));
    await waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]).toMatchObject({
      method: 'POST',
      body: { expectedRevision: 2 },
    });
    expect(await screen.findByText('Application submitted')).toBeInTheDocument();
    expect(screen.getByLabelText(/Applicant name/)).toBeDisabled();
    expect(screen.getByText('Rule passed')).toBeInTheDocument();
  });

  it('keeps a rule mismatch in draft and lets the user correct it', async () => {
    const user = userEvent.setup();
    const draft = applicationRecord({
      values: { applicant_name: [''] },
      revision: 1,
      status: 'Draft',
    });
    const mismatch = applicationRecord({
      values: { applicant_name: [''] },
      revision: 1,
      status: 'Draft',
      ruleEvaluations: [
        {
          ...matchedEvaluation(),
          isMatch: false,
          diagnostics: [{ nodeId: 'required', isMatch: false }],
        },
      ],
    });

    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const path = requestPath(input);
      const method = init?.method ?? 'GET';
      if (path === `/api/business-object-records/${recordId}` && method === 'GET') {
        return jsonResponse(draft);
      }
      if (path === `/api/business-object-records/${recordId}/submit` && method === 'POST') {
        return jsonResponse({
          isSubmitted: false,
          record: mismatch,
          ruleEvaluations: mismatch.ruleEvaluations,
        });
      }
      throw new Error(`Unexpected ${method} ${path}`);
    });

    const queryClient = renderWorkflowWindow();
    await waitFor(() => {
      expect(queryClient.getQueryState(applicationQueryKeys.detail(recordId))?.status).toBe(
        'success',
      );
    });
    await screen.findByLabelText(/Applicant name/);
    await user.click(screen.getByRole('button', { name: 'Submit application' }));

    expect(await screen.findByText('Some rules need attention')).toBeInTheDocument();
    expect(screen.getByText('Needs attention')).toBeInTheDocument();
    expect(screen.getByLabelText(/Applicant name/)).not.toBeDisabled();
  });
});

function renderWorkflowWindow() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function WindowLauncher() {
    const { openWindow } = useManagedWindowActions();

    return (
      <button
        type="button"
        onClick={() =>
          openWindow(applicationRecordWindowDescriptor({ recordId, title: 'Loan application' }))
        }
      >
        Open application
      </button>
    );
  }

  function WorkflowHarness() {
    const { openWindow } = useManagedWindowActions();
    const descriptor = applicationRecordWindowDescriptor({
      recordId,
      title: 'Loan application',
    });

    return (
      <>
        <WindowLauncher />
        <button type="button" onClick={() => openWindow(descriptor)}>
          Launch workflow
        </button>
      </>
    );
  }

  act(() => {
    render(
      <QueryClientProvider client={queryClient}>
        <ManagedWindowProvider
          renderers={{ ...managedWindowRenderers, ...applicationsManagedWindowRenderers }}
        >
          <div className="relative h-dvh w-dvw">
            <WorkflowHarness />
            <ManagedWindowHost />
          </div>
        </ManagedWindowProvider>
      </QueryClientProvider>,
    );
  });

  act(() => {
    screen.getByRole('button', { name: 'Launch workflow' }).click();
  });

  return queryClient;
}

function applicationRecord(
  overrides: Partial<BusinessObjectRecordDetail> = {},
): BusinessObjectRecordDetail {
  return {
    id: recordId,
    workspaceId: '33333333-3333-4333-8333-333333333333',
    objectKey: 'loan_application',
    definitionVersion: 1,
    definitionVersionId: '44444444-4444-4444-8444-444444444444',
    status: 'Draft',
    revision: 1,
    values: {},
    fields: [
      {
        fieldKey: 'applicant_name',
        label: 'Applicant name',
        order: 1,
        fieldType: 'Text',
        rules: [{ bindingId, bindingRevision: 1 }],
      },
      {
        fieldKey: 'contact_email',
        label: 'Contact email',
        order: 2,
        fieldType: 'Text',
        rules: [],
      },
      {
        fieldKey: 'requested_amount',
        label: 'Requested amount',
        order: 3,
        fieldType: 'Integer',
        rules: [],
      },
      {
        fieldKey: 'purpose',
        label: 'Purpose',
        order: 4,
        fieldType: 'Text',
        rules: [],
      },
    ],
    ruleEvaluations: [],
    createdByUserId: '55555555-5555-4555-8555-555555555555',
    createdAt: '2026-08-02T09:00:00Z',
    updatedByUserId: '55555555-5555-4555-8555-555555555555',
    updatedAt: '2026-08-02T09:00:00Z',
    submittedByUserId: null,
    submittedAt: null,
    ...overrides,
  };
}

function matchedEvaluation(): BusinessObjectRecordRuleEvaluation {
  return {
    fieldKey: 'applicant_name',
    bindingId,
    bindingRevision: 1,
    definitionKey: 'field.required',
    definitionVersion: 1,
    isMatch: true,
    diagnostics: [{ nodeId: 'required', isMatch: true }],
  };
}

function jsonResponse(data: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: () => Promise.resolve(JSON.stringify(data)),
    json: () => Promise.resolve(data),
  } as Response;
}

function requestPath(input: RequestInfo | URL): string {
  const value = typeof input === 'string' ? input : input.toString();
  return new URL(value, 'https://axis.test').pathname;
}
