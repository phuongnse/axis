import { expect, type Page, type Route, test } from '@playwright/test';
import type * as ApiTypes from '../src/lib/api-generated';

const profile = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'delivery-governance@example.com',
  fullName: 'Delivery Governance Administrator',
  isActive: true,
  language: 'en',
  theme: 'light',
  workspaceId: '22222222-2222-4222-8222-222222222222',
  workspaces: [
    {
      id: '22222222-2222-4222-8222-222222222222',
      name: 'Delivery workspace',
      slug: 'delivery-workspace',
      type: 'Organization',
      isCurrent: true,
    },
  ],
};

function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>((next) => {
    resolve = next;
  });
  return { promise, resolve };
}

function watchPageErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() !== 'error') return;
    const text = message.text();
    if (text === 'Failed to load resource: the server responded with a status of 409 (Conflict)') {
      return;
    }
    errors.push(text);
  });
  page.on('pageerror', (error) => errors.push(error.message));
  return errors;
}

async function fulfillJson(route: Route, status: number, body: unknown): Promise<void> {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  });
}

async function mockAuthenticatedSession(page: Page): Promise<void> {
  await page.addInitScript(() => {
    (window as Window & { __AXIS_DISABLE_DEVTOOLS__?: boolean }).__AXIS_DISABLE_DEVTOOLS__ = true;
    localStorage.setItem('axis.language', 'en');
    localStorage.setItem('axis.theme', 'light');
  });

  await page.route('**/api/auth/session', (route) =>
    fulfillJson(route, 200, {
      authenticated: true,
      csrfToken: 'delivery-governance-csrf-token',
      user: {
        userId: profile.id,
        workspaceId: profile.workspaceId,
        email: profile.email,
        name: profile.fullName,
      },
    }),
  );
  await page.route('**/api/users/me', (route) => fulfillJson(route, 200, profile));
  await page.route('**/api/workspace-context/eligible', (route) =>
    fulfillJson(route, 200, [
      {
        workspaceId: profile.workspaceId,
        name: 'Delivery workspace',
        slug: 'delivery-workspace',
        type: 'Organization',
        organizationId: '33333333-3333-4333-8333-333333333333',
        isCurrent: true,
      },
    ]),
  );
}

test('manage-workspace-service-identities AT-006 recovers a key conflict and revokes the key', async ({
  page,
}) => {
  const pageErrors = watchPageErrors(page);
  const firstAddStarted = deferred();
  const releaseFirstAdd = deferred();
  const mutations: Array<{ path: string; body: Record<string, unknown> }> = [];
  let addAttempts = 0;
  let identity: ApiTypes.ServiceIdentityDto = {
    id: 'service-1',
    clientId: 'invoice-worker',
    workspaceId: profile.workspaceId,
    status: 'Active',
    workspaceGrantStatus: 'Active',
    revision: 2,
    subject: { kind: 'Service', subjectId: 'service-1' },
    keys: [],
  };

  await mockAuthenticatedSession(page);
  await page.route('**/api/service-identities**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() === 'GET' && path === '/api/service-identities') {
      await fulfillJson(route, 200, [identity]);
      return;
    }
    if (request.method() === 'POST' && path === '/api/service-identities/service-1/keys') {
      const body = request.postDataJSON() as Record<string, unknown>;
      mutations.push({ path, body });
      addAttempts += 1;
      if (addAttempts === 1) {
        firstAddStarted.resolve();
        await releaseFirstAdd.promise;
        identity = {
          ...identity,
          revision: 3,
          keys: [
            {
              id: 'key-concurrent',
              kid: 'concurrent-kid',
              thumbprint: 'concurrent-thumbprint',
              status: 'Active',
            },
          ],
        };
        await fulfillJson(route, 409, {
          title: 'Conflict',
          status: 409,
          code: 'identity.service_identity.revision_conflict',
        });
        return;
      }
      identity = {
        ...identity,
        revision: 4,
        keys: [
          ...(identity.keys ?? []),
          {
            id: 'key-rotation',
            kid: 'rotation-2026-08',
            thumbprint: 'rotation-thumbprint',
            status: 'Active',
          },
        ],
      };
      await fulfillJson(route, 200, identity);
      return;
    }
    if (
      request.method() === 'POST' &&
      path === '/api/service-identities/service-1/keys/key-rotation/revoke'
    ) {
      const body = request.postDataJSON() as Record<string, unknown>;
      mutations.push({ path, body });
      identity = {
        ...identity,
        revision: 5,
        keys: (identity.keys ?? []).map((key) =>
          key.id === 'key-rotation' ? { ...key, status: 'Revoked' } : key,
        ),
      };
      await fulfillJson(route, 200, identity);
      return;
    }
    await fulfillJson(route, 404, {});
  });

  await page.goto('/service-identities');
  await expect(page).toHaveURL(/\/service-identities$/);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Service identities', exact: true }),
  ).toBeVisible();

  const publicJwk = JSON.stringify({
    kty: 'EC',
    crv: 'P-256',
    kid: 'rotation-2026-08',
    x: 'public-x-coordinate',
    y: 'public-y-coordinate',
  });
  const jwkInput = page.getByRole('textbox', { name: 'Public ES256 JWK' });
  await jwkInput.fill(publicJwk);
  const addKey = page.getByRole('button', { name: 'Add public key' });
  await addKey.focus();
  await expect(addKey).toBeFocused();
  await page.keyboard.press('Enter');
  await firstAddStarted.promise;
  await expect(addKey).toBeDisabled();
  await expect(jwkInput).toBeDisabled();
  releaseFirstAdd.resolve();
  await expect(page.getByText('Identity changed')).toBeVisible();
  await expect(page.getByText('Reload the current revision before trying again.')).toBeVisible();

  await page.reload();
  await expect(page).toHaveURL(/\/service-identities$/);
  await expect(page.getByText('concurrent-kid')).toBeVisible();
  await expect(page.getByText('3', { exact: true })).toBeVisible();
  await jwkInput.fill(publicJwk);
  await addKey.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByText('Public key added')).toBeVisible();
  await expect(page.getByText('rotation-2026-08')).toBeVisible();

  const revokeKey = page.getByRole('button', { name: 'Revoke key' }).last();
  await revokeKey.focus();
  await page.keyboard.press('Enter');
  const revokeDialog = page.getByRole('alertdialog', { name: 'Revoke this public key?' });
  await expect(revokeDialog).toBeVisible();
  await expect
    .poll(() => revokeDialog.evaluate((element) => element.contains(document.activeElement)))
    .toBe(true);
  const confirmRevoke = revokeDialog.getByRole('button', { name: 'Revoke key' });
  await confirmRevoke.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByText('Key revoked')).toBeVisible();
  await expect(
    page.getByText('rotation-2026-08', { exact: true }).locator('..').locator('..'),
  ).toContainText('Revoked');

  expect(mutations).toEqual([
    {
      path: '/api/service-identities/service-1/keys',
      body: { expectedRevision: 2, publicJwk },
    },
    {
      path: '/api/service-identities/service-1/keys',
      body: { expectedRevision: 3, publicJwk },
    },
    {
      path: '/api/service-identities/service-1/keys/key-rotation/revoke',
      body: { expectedRevision: 4 },
    },
  ]);
  expect(pageErrors).toEqual([]);
});

test('manage-product-role-assignments AT-005 reloads a conflict then assigns and revokes the exact role', async ({
  page,
}) => {
  const pageErrors = watchPageErrors(page);
  const firstAssignStarted = deferred();
  const releaseFirstAssign = deferred();
  const mutations: Array<{
    path: string;
    idempotencyKey: string | null;
    body: Record<string, unknown>;
  }> = [];
  const subject: ApiTypes.AssignableSubjectDto = {
    subject: { kind: 'Service', subjectId: 'service-1' },
    displayName: 'Invoice worker',
    secondaryLabel: 'invoice-worker',
  };
  const role: ApiTypes.ProductRoleOptionDto = {
    policyVersionId: 'policy-version-7',
    policyKey: 'invoice-operations',
    roleKey: 'invoice.approver',
    displayName: 'Invoice approver',
    description: 'Approves invoices for payment.',
  };
  let assignments: ApiTypes.ProductRoleAssignmentDto[] = [];
  let assignAttempts = 0;

  await mockAuthenticatedSession(page);
  await page.route('**/api/product-role-assignments**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() === 'GET' && path === '/api/product-role-assignments') {
      await fulfillJson(route, 200, { subjects: [subject], roles: [role], assignments });
      return;
    }
    if (request.method() === 'POST') {
      const body = request.postDataJSON() as Record<string, unknown>;
      mutations.push({
        path,
        idempotencyKey: await request.headerValue('idempotency-key'),
        body,
      });
      if (path === '/api/product-role-assignments/assign') {
        assignAttempts += 1;
        if (assignAttempts === 1) {
          firstAssignStarted.resolve();
          await releaseFirstAssign.promise;
          assignments = [
            {
              workspaceId: profile.workspaceId,
              subject: subject.subject,
              policyVersionId: role.policyVersionId,
              roleKey: role.roleKey,
              isActive: true,
              revision: 4,
            },
          ];
          await fulfillJson(route, 409, {
            title: 'Conflict',
            status: 409,
            code: 'authorization.product_role_assignment.revision_conflict',
          });
          return;
        }
        await fulfillJson(route, 200, assignments[0]);
        return;
      }
      if (path === '/api/product-role-assignments/revoke') {
        assignments = [{ ...assignments[0], isActive: false, revision: 5 }];
        await fulfillJson(route, 200, assignments[0]);
        return;
      }
    }
    await fulfillJson(route, 404, {});
  });

  async function selectExactAssignment(): Promise<void> {
    const subjectSelect = page.getByRole('combobox', { name: 'Active subject' });
    await subjectSelect.focus();
    await expect(subjectSelect).toBeFocused();
    await page.keyboard.press('Enter');
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Enter');
    await expect(subjectSelect).toContainText('Invoice worker');

    const roleSelect = page.getByRole('combobox', { name: 'Installed product role' });
    await roleSelect.focus();
    await page.keyboard.press('Enter');
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Enter');
    await expect(roleSelect).toContainText('Invoice approver');
    await expect(
      page.getByLabel('Assign product role').getByText('Approves invoices for payment.'),
    ).toBeVisible();
  }

  await page.goto('/product-role-assignments');
  await expect(page).toHaveURL(/\/product-role-assignments$/);
  await expect(
    page.getByRole('heading', { name: 'Product-role assignments', exact: true }),
  ).toBeVisible();
  await selectExactAssignment();
  const assignRole = page.getByRole('button', { name: 'Assign role' });
  await assignRole.focus();
  await page.keyboard.press('Enter');
  await firstAssignStarted.promise;
  await expect(page.getByRole('button', { name: 'Assigning…' })).toBeDisabled();
  await expect(page.getByRole('combobox', { name: 'Active subject' })).toBeDisabled();
  releaseFirstAssign.resolve();
  await expect(page.getByText('Assignment changed')).toBeVisible();
  await expect(
    page.getByText('Reload the authoritative revision before trying again.'),
  ).toBeVisible();

  await page.reload();
  await expect(page).toHaveURL(/\/product-role-assignments$/);
  await expect(page.getByRole('list', { name: 'Current product-role assignments' })).toContainText(
    'Invoice approver',
  );
  await selectExactAssignment();
  await assignRole.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByText('Product role assigned')).toBeVisible();

  const revokeRole = page.getByRole('button', { name: 'Revoke role' });
  await revokeRole.focus();
  await page.keyboard.press('Enter');
  const revokeDialog = page.getByRole('alertdialog', {
    name: 'Revoke this exact product role?',
  });
  await expect(revokeDialog).toContainText('Revoke Invoice approver from Invoice worker.');
  await expect
    .poll(() => revokeDialog.evaluate((element) => element.contains(document.activeElement)))
    .toBe(true);
  const confirmRevoke = revokeDialog.getByRole('button', { name: 'Revoke role' });
  await confirmRevoke.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByText('Product role revoked')).toBeVisible();
  await expect(page.getByText('No active product-role assignments.')).toBeVisible();

  expect(mutations.map(({ path, body }) => ({ path, body }))).toEqual([
    {
      path: '/api/product-role-assignments/assign',
      body: {
        target: { kind: 'Service', subjectId: 'service-1' },
        policyVersionId: 'policy-version-7',
        roleKey: 'invoice.approver',
        expectedRevision: null,
      },
    },
    {
      path: '/api/product-role-assignments/assign',
      body: {
        target: { kind: 'Service', subjectId: 'service-1' },
        policyVersionId: 'policy-version-7',
        roleKey: 'invoice.approver',
        expectedRevision: 4,
      },
    },
    {
      path: '/api/product-role-assignments/revoke',
      body: {
        target: { kind: 'Service', subjectId: 'service-1' },
        policyVersionId: 'policy-version-7',
        roleKey: 'invoice.approver',
        expectedRevision: 4,
      },
    },
  ]);
  expect(mutations.every(({ idempotencyKey }) => Boolean(idempotencyKey))).toBe(true);
  expect(pageErrors).toEqual([]);
});

test('solution management proves publish AT-005 and install AT-007 recovery', async ({ page }) => {
  const pageErrors = watchPageErrors(page);
  const firstPublishStarted = deferred();
  const releaseFirstPublish = deferred();
  const resumeStarted = deferred();
  const releaseResume = deferred();
  const publishedBytes: string[] = [];
  const publishContentTypes: Array<string | null> = [];
  const installIdempotencyKeys: Array<string | null> = [];
  const version: ApiTypes.SolutionVersionSummaryDto = {
    id: 'solution-version-1',
    solutionKey: 'invoice-operations',
    solutionVersion: '1.0.0',
    packageSha256: 'package-sha256-safe-readback',
    axisOpenApiSha256: 'axis-openapi-sha256',
    publisherId: 'axis-reference-publisher',
    publisherKeyId: 'publisher-key-2026-08',
    trustStatus: 'Trusted',
    sourceRevision: 'abc123def456',
    buildId: 'build-2026-08-08',
    sourceUri: 'https://example.test/invoice-operations',
    components: [
      {
        type: 'authorization.policy.v1',
        key: 'invoice-policy',
        sha256: 'policy-sha256',
        dependsOn: [],
      },
      {
        type: 'business-object.definition.v1',
        key: 'invoice-definition',
        sha256: 'definition-sha256',
        dependsOn: [{ type: 'authorization.policy.v1', key: 'invoice-policy' }],
      },
      {
        type: 'rule.binding.v1',
        key: 'invoice-total-binding',
        sha256: 'binding-sha256',
        dependsOn: [{ type: 'business-object.definition.v1', key: 'invoice-definition' }],
      },
    ],
  };
  const failedOperation: ApiTypes.SolutionOperationStatusDto = {
    id: 'operation-1',
    installationId: 'installation-1',
    status: 'Failed',
    leaseEpoch: 2,
    problemCode: 'solutions.adapter.readback_unavailable',
    steps: [
      {
        type: 'authorization.policy.v1',
        key: 'invoice-policy',
        sha256: 'policy-sha256',
        status: 'Confirmed',
      },
      {
        type: 'business-object.definition.v1',
        key: 'invoice-definition',
        sha256: 'definition-sha256',
        status: 'Failed',
        problemCode: 'solutions.adapter.readback_unavailable',
      },
      {
        type: 'rule.binding.v1',
        key: 'invoice-total-binding',
        sha256: 'binding-sha256',
        status: 'Pending',
      },
    ],
  };
  const runningOperation: ApiTypes.SolutionOperationStatusDto = {
    ...failedOperation,
    status: 'Running',
    leaseEpoch: 3,
    problemCode: null,
    steps: (failedOperation.steps ?? []).map((step) => ({
      ...step,
      status: step.key === 'invoice-definition' ? 'Applying' : step.status,
      problemCode: null,
    })),
  };
  const succeededOperation: ApiTypes.SolutionOperationStatusDto = {
    ...runningOperation,
    status: 'Succeeded',
    steps: (runningOperation.steps ?? []).map((step) => ({ ...step, status: 'Confirmed' })),
  };
  let versions: ApiTypes.SolutionVersionSummaryDto[] = [];
  let operation: ApiTypes.SolutionOperationStatusDto | undefined;
  let publishAttempts = 0;
  let resumed = false;

  const installation = (): ApiTypes.SolutionInstallationStatusDto[] =>
    operation
      ? [
          {
            id: 'installation-1',
            workspaceId: profile.workspaceId,
            solutionVersionId: version.id,
            operationId: operation.id,
            operationStatus: operation.status,
            provisioningStatus: operation.status === 'Succeeded' ? 'Installed' : 'Failed',
            complianceStatus: 'Compliant',
            components: operation.steps,
          },
        ]
      : [];

  await mockAuthenticatedSession(page);
  await page.route('**/api/solutions/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() === 'GET' && path === '/api/solutions/versions') {
      await fulfillJson(route, 200, versions);
      return;
    }
    if (request.method() === 'GET' && path === '/api/solutions/installations') {
      await fulfillJson(route, 200, installation());
      return;
    }
    if (request.method() === 'POST' && path === '/api/solutions/versions') {
      publishAttempts += 1;
      publishedBytes.push(request.postData() ?? '');
      publishContentTypes.push(await request.headerValue('content-type'));
      if (publishAttempts === 1) {
        firstPublishStarted.resolve();
        await releaseFirstPublish.promise;
        await fulfillJson(route, 409, {
          title: 'Conflict',
          status: 409,
          code: 'solutions.version.identity_conflict',
        });
        return;
      }
      versions = [version];
      await fulfillJson(route, 200, { version, isRetry: true });
      return;
    }
    if (
      request.method() === 'POST' &&
      path === '/api/solutions/versions/solution-version-1/installations'
    ) {
      installIdempotencyKeys.push(await request.headerValue('idempotency-key'));
      operation = failedOperation;
      await fulfillJson(route, 201, { operation, isRetry: false });
      return;
    }
    if (request.method() === 'GET' && path === '/api/solutions/operations/operation-1') {
      if (resumed) operation = succeededOperation;
      await fulfillJson(route, 200, operation ?? failedOperation);
      return;
    }
    if (request.method() === 'POST' && path === '/api/solutions/operations/operation-1/resume') {
      resumeStarted.resolve();
      await releaseResume.promise;
      resumed = true;
      operation = runningOperation;
      await fulfillJson(route, 200, runningOperation);
      return;
    }
    await fulfillJson(route, 404, {});
  });

  await page.goto('/solutions');
  await expect(page).toHaveURL(/\/solutions$/);
  await expect(page.getByRole('heading', { name: 'Solutions', exact: true })).toBeVisible();

  const packageBytes = 'signed-envelope-browser-evidence';
  await page.getByLabel('Signed solution package').setInputFiles({
    name: 'invoice-operations.axis-solution',
    mimeType: 'application/vnd.dsse.envelope.v1+json',
    buffer: Buffer.from(packageBytes),
  });
  const publishPackage = page
    .getByLabel('Publish signed version')
    .getByRole('button', { name: 'Publish package' });
  await publishPackage.focus();
  await page.keyboard.press('Enter');
  let confirmation = page.getByRole('alertdialog', { name: 'Publish this signed package?' });
  await expect(confirmation).toContainText('immutable release');
  await expect
    .poll(() => confirmation.evaluate((element) => element.contains(document.activeElement)))
    .toBe(true);
  await confirmation.getByRole('button', { name: 'Publish package' }).focus();
  await page.keyboard.press('Enter');
  await firstPublishStarted.promise;
  await expect(page.getByRole('button', { name: 'Verifying and publishing…' })).toBeDisabled();
  releaseFirstPublish.resolve();
  await expect(page.getByText('Solution changed')).toBeVisible();
  await expect(page.getByText('Refresh the authoritative state before retrying.')).toBeVisible();

  await publishPackage.focus();
  await page.keyboard.press('Enter');
  confirmation = page.getByRole('alertdialog', { name: 'Publish this signed package?' });
  await confirmation.getByRole('button', { name: 'Publish package' }).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByText('Solution version published')).toBeVisible();
  await expect(
    page.getByText(
      'The exact immutable version already existed; its canonical safe result was returned.',
    ),
  ).toBeVisible();
  const release = page.getByRole('region', { name: 'Selected release' });
  await expect(release).toContainText('invoice-operations');
  await expect(release).toContainText('1.0.0');
  await expect(release).toContainText('package-sha256-safe-readback');
  await expect(release).toContainText('axis-reference-publisher');
  await expect(release).toContainText('publisher-key-2026-08');
  await expect(release).toContainText('axis-openapi-sha256');
  await expect(page.getByText(packageBytes)).toHaveCount(0);

  const installVersion = page.getByRole('button', { name: 'Install version' });
  await installVersion.focus();
  await page.keyboard.press('Enter');
  confirmation = page.getByRole('alertdialog', { name: 'Install this immutable version?' });
  await expect(confirmation).toContainText('invoice-operations 1.0.0');
  await confirmation.getByRole('button', { name: 'Install version' }).focus();
  await page.keyboard.press('Enter');
  const progress = page.getByRole('region', { name: 'Installation progress' });
  await expect(progress).toContainText('Failed');
  await expect(progress).toContainText('invoice-policy');
  await expect(progress).toContainText('Confirmed');
  await expect(progress).toContainText('invoice-total-binding');
  await expect(progress).toContainText('Pending');
  await expect(progress).toContainText('solutions.adapter.readback_unavailable');

  const resumeOperation = progress.getByRole('button', { name: 'Resume operation' });
  await resumeOperation.focus();
  await expect(resumeOperation).toBeFocused();
  await page.keyboard.press('Enter');
  await resumeStarted.promise;
  await expect(progress.getByRole('button', { name: 'Resuming…' })).toBeDisabled();
  releaseResume.resolve();
  await expect(page.getByText('Resume accepted')).toBeVisible();
  await expect(progress).toContainText('Succeeded');
  await expect(progress.getByText('Confirmed')).toHaveCount(3);
  const installations = page.getByRole('region', { name: 'Workspace installations' });
  await expect(installations).toContainText('Installed');
  await expect(installations).toContainText('Compliant');

  expect(publishedBytes).toEqual([packageBytes, packageBytes]);
  expect(publishContentTypes).toEqual([
    'application/vnd.dsse.envelope.v1+json',
    'application/vnd.dsse.envelope.v1+json',
  ]);
  expect(installIdempotencyKeys).toHaveLength(1);
  expect(installIdempotencyKeys[0]).toBeTruthy();
  expect(pageErrors).toEqual([]);
});
