import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { PackageCheck, Play, RotateCw, Upload } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AsyncButton } from '@/components/shared/AsyncButton';
import { AsyncContent } from '@/components/shared/AsyncContent';
import { ProcessWorkbench } from '@/components/shared/ProcessWorkbench';
import { StatusBadge, type StatusBadgeTone } from '@/components/shared/StatusBadge';
import { StatusNotice, type StatusNoticeTone } from '@/components/shared/StatusNotice';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog';
import { Button } from '@/components/ui/button';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { ApiError } from '@/lib/api';
import type {
  SolutionComponentPlanDto,
  SolutionComponentStatusDto,
  SolutionInstallationStatusDto,
  SolutionOperationStatus,
  SolutionOperationStatusDto,
  SolutionProvisioningStatus,
  SolutionStepStatus,
  SolutionVersionSummaryDto,
} from '@/lib/api-generated';
import {
  installSolutionVersion,
  publishSolutionVersion,
  resumeSolutionOperation,
  solutionInstallationsQueryOptions,
  solutionOperationQueryOptions,
  solutionPackageMaxBytes,
  solutionQueryKeys,
  solutionVersionsQueryOptions,
} from '../api';

type Feedback = {
  tone: StatusNoticeTone;
  title: string;
  body: string;
  retryPublish?: boolean;
};

export function SolutionsPage() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [fileInputKey, setFileInputKey] = useState(0);
  const [publishedVersion, setPublishedVersion] = useState<SolutionVersionSummaryDto | null>(null);
  const [installKey, setInstallKey] = useState('');
  const [operationId, setOperationId] = useState('');
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [publishConfirmOpen, setPublishConfirmOpen] = useState(false);
  const [installConfirmOpen, setInstallConfirmOpen] = useState(false);
  const installationsQuery = useQuery(solutionInstallationsQueryOptions());
  const versionsQuery = useQuery(solutionVersionsQueryOptions());
  const operationQuery = useQuery(solutionOperationQueryOptions(operationId));

  const publishMutation = useMutation({
    mutationFn: publishSolutionVersion,
    onSuccess: (result) => {
      setPublishedVersion(result.version ?? null);
      setInstallKey(createIdempotencyKey());
      setFile(null);
      setFileInputKey((current) => current + 1);
      setFeedback({
        tone: 'success',
        title: t('solutions.publishSucceeded'),
        body: result.isRetry
          ? t('solutions.publishCanonicalRetry')
          : t('solutions.publishSucceededDescription'),
      });
      void queryClient.invalidateQueries({ queryKey: solutionQueryKeys.versions() });
    },
    onError: (error) => setFeedback(publishProblemFeedback(error, t)),
  });

  const installMutation = useMutation({
    mutationFn: ({ versionId, key }: { versionId: string; key: string }) =>
      installSolutionVersion(versionId, key),
    onSuccess: async (result) => {
      const nextOperationId = result.operation?.id ?? '';
      setOperationId(nextOperationId);
      if (nextOperationId) {
        queryClient.setQueryData(solutionQueryKeys.operation(nextOperationId), result.operation);
      }
      setFeedback({
        tone: 'success',
        title: t('solutions.installStarted'),
        body: result.isRetry
          ? t('solutions.installCanonicalRetry')
          : t('solutions.installStartedDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: solutionQueryKeys.installations() });
    },
    onError: (error) => setFeedback(solutionProblemFeedback(error, t)),
  });

  const resumeMutation = useMutation({
    mutationFn: resumeSolutionOperation,
    onSuccess: async (result) => {
      if (result.id) {
        setOperationId(result.id);
        queryClient.setQueryData(solutionQueryKeys.operation(result.id), result);
      }
      setFeedback({
        tone: 'info',
        title: t('solutions.resumeAccepted'),
        body: t('solutions.resumeAcceptedDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: solutionQueryKeys.installations() });
    },
    onError: (error) => setFeedback(solutionProblemFeedback(error, t)),
  });

  const operation = operationQuery.data;
  useEffect(() => {
    if (operation?.status === 'Succeeded' || operation?.status === 'Failed') {
      void queryClient.invalidateQueries({ queryKey: solutionQueryKeys.installations() });
    }
  }, [operation?.status, queryClient]);

  const pending =
    publishMutation.isPending || installMutation.isPending || resumeMutation.isPending;
  const installable =
    publishedVersion?.id !== undefined && publishedVersion.trustStatus === 'Trusted';

  function selectFile(selected: File | null) {
    setFeedback(null);
    setPublishedVersion(null);
    setOperationId('');
    if (!selected) {
      setFile(null);
      setFileError(null);
      return;
    }
    if (selected.size > solutionPackageMaxBytes) {
      setFile(null);
      setFileError(t('solutions.packageTooLarge'));
      return;
    }
    if (selected.size === 0) {
      setFile(null);
      setFileError(t('solutions.packageEmpty'));
      return;
    }
    setFile(selected);
    setFileError(null);
  }

  return (
    <ProcessWorkbench
      surfaceId="solution-delivery"
      title={t('solutions.title')}
      description={t('solutions.description')}
    >
      {feedback ? (
        <div aria-live="polite">
          <StatusNotice tone={feedback.tone} title={feedback.title}>
            {feedback.retryPublish && file ? (
              <div className="grid gap-2">
                <span>{feedback.body}</span>
                <AsyncButton
                  type="button"
                  size="sm"
                  variant="outline"
                  className="w-fit"
                  disabled={publishMutation.isPending}
                  onClick={() => publishMutation.mutate(file)}
                  icon={<RotateCw aria-hidden />}
                  pending={publishMutation.isPending}
                  pendingLabel={t('solutions.publishing')}
                >
                  {t('app.retry')}
                </AsyncButton>
              </div>
            ) : (
              feedback.body
            )}
          </StatusNotice>
        </div>
      ) : null}

      <section className="grid gap-4 border-b border-border pb-5" aria-labelledby="publish-title">
        <div>
          <h2 id="publish-title" className="text-lg font-medium">
            {t('solutions.publishTitle')}
          </h2>
          <p className="text-sm text-muted-foreground">{t('solutions.publishDescription')}</p>
        </div>
        <div className="grid gap-4 lg:grid-cols-2 lg:items-end">
          <Field data-invalid={Boolean(fileError)}>
            <FieldLabel htmlFor="solution-package">{t('solutions.packageLabel')}</FieldLabel>
            <Input
              key={fileInputKey}
              id="solution-package"
              type="file"
              disabled={pending}
              aria-invalid={Boolean(fileError)}
              aria-describedby={fileError ? 'solution-package-error' : 'solution-package-help'}
              onChange={(event) => selectFile(event.target.files?.[0] ?? null)}
            />
            {fileError ? (
              <FieldError id="solution-package-error">{fileError}</FieldError>
            ) : (
              <FieldDescription id="solution-package-help">
                {t('solutions.packageHelp')}
              </FieldDescription>
            )}
          </Field>
          <AlertDialog open={publishConfirmOpen} onOpenChange={setPublishConfirmOpen}>
            <AlertDialogTrigger
              render={
                <AsyncButton
                  type="button"
                  disabled={!file || pending}
                  icon={<Upload aria-hidden />}
                  pending={publishMutation.isPending}
                  pendingLabel={t('solutions.publishing')}
                >
                  {t('solutions.publishAction')}
                </AsyncButton>
              }
            />
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>{t('solutions.publishConfirmTitle')}</AlertDialogTitle>
                <AlertDialogDescription>
                  {t('solutions.publishConfirmDescription', {
                    name: file?.name ?? '',
                    size: file ? formatBytes(file.size) : '',
                  })}
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>{t('app.cancel')}</AlertDialogCancel>
                <AlertDialogAction
                  onClick={() => {
                    if (file) {
                      setPublishConfirmOpen(false);
                      publishMutation.mutate(file);
                    }
                  }}
                >
                  {t('solutions.publishAction')}
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </div>
        {file ? (
          <p className="text-sm" aria-live="polite">
            {t('solutions.packageSelected', { name: file.name, size: formatBytes(file.size) })}
          </p>
        ) : null}
      </section>

      <section className="grid gap-4 border-b border-border pb-5" aria-labelledby="versions-title">
        <div>
          <h2 id="versions-title" className="text-lg font-medium">
            {t('solutions.versionsTitle')}
          </h2>
          <p className="text-sm text-muted-foreground">{t('solutions.versionsDescription')}</p>
        </div>
        <AsyncContent
          pending={versionsQuery.isPending}
          error={versionsQuery.isError}
          pendingLabel={t('solutions.versionsLoading')}
        >
          {versionsQuery.isError ? (
            <StatusNotice tone="destructive" title={t('solutions.versionsLoadFailed')}>
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() => void versionsQuery.refetch()}
              >
                {t('app.retry')}
              </Button>
            </StatusNotice>
          ) : (versionsQuery.data ?? []).length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('solutions.versionsEmpty')}</p>
          ) : (
            <ul className="grid gap-2" aria-label={t('solutions.versionsListLabel')}>
              {(versionsQuery.data ?? []).map((version) => (
                <li key={version.id}>
                  <Button
                    type="button"
                    variant={version.id === publishedVersion?.id ? 'secondary' : 'ghost'}
                    className="h-auto w-full justify-between py-2 text-left"
                    onClick={() => {
                      setPublishedVersion(version);
                      setInstallKey(createIdempotencyKey());
                      setOperationId('');
                    }}
                  >
                    <span className="min-w-0">
                      <span className="block truncate">
                        {version.solutionKey} {version.solutionVersion}
                      </span>
                      <span className="block truncate text-xs text-muted-foreground">
                        {version.packageSha256}
                      </span>
                    </span>
                    <StatusBadge tone={trustTone(version.trustStatus)}>
                      {solutionTrustLabel(version.trustStatus, t)}
                    </StatusBadge>
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </AsyncContent>
      </section>

      {publishedVersion ? (
        <section className="grid gap-4 border-b border-border pb-5" aria-labelledby="release-title">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 id="release-title" className="text-lg font-medium">
                {t('solutions.releaseTitle')}
              </h2>
              <p className="text-sm text-muted-foreground">{t('solutions.releaseDescription')}</p>
            </div>
            <StatusBadge tone={trustTone(publishedVersion.trustStatus)}>
              {solutionTrustLabel(publishedVersion.trustStatus, t)}
            </StatusBadge>
          </div>
          <VersionFacts version={publishedVersion} />
          <ComponentPlan components={publishedVersion.components ?? []} />
          {installable ? (
            <AlertDialog open={installConfirmOpen} onOpenChange={setInstallConfirmOpen}>
              <AlertDialogTrigger
                render={
                  <AsyncButton
                    type="button"
                    className="w-fit"
                    disabled={pending}
                    icon={<PackageCheck aria-hidden />}
                    pending={installMutation.isPending}
                    pendingLabel={t('solutions.installing')}
                  >
                    {t('solutions.installAction')}
                  </AsyncButton>
                }
              />
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>{t('solutions.installConfirmTitle')}</AlertDialogTitle>
                  <AlertDialogDescription>
                    {t('solutions.installConfirmDescription', {
                      solution: publishedVersion.solutionKey ?? t('solutions.notAvailable'),
                      version: publishedVersion.solutionVersion ?? t('solutions.notAvailable'),
                    })}
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>{t('app.cancel')}</AlertDialogCancel>
                  <AlertDialogAction
                    onClick={() => {
                      if (publishedVersion.id) {
                        setInstallConfirmOpen(false);
                        installMutation.mutate({
                          versionId: publishedVersion.id,
                          key: installKey || createIdempotencyKey(),
                        });
                      }
                    }}
                  >
                    {t('solutions.installAction')}
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          ) : (
            <StatusNotice tone="warning" title={t('solutions.installUnavailable')}>
              {t('solutions.installUnavailableDescription')}
            </StatusNotice>
          )}
        </section>
      ) : null}

      {operationId ? (
        <OperationPanel
          operation={operation}
          loading={operationQuery.isPending}
          error={operationQuery.isError}
          resuming={resumeMutation.isPending}
          onRetry={() => void operationQuery.refetch()}
          onResume={() => resumeMutation.mutate(operationId)}
        />
      ) : null}

      <InstallationsPanel
        installations={installationsQuery.data ?? []}
        loading={installationsQuery.isPending}
        error={installationsQuery.isError}
        onRetry={() => void installationsQuery.refetch()}
        onOpenOperation={setOperationId}
      />
    </ProcessWorkbench>
  );
}

function VersionFacts({ version }: { version: SolutionVersionSummaryDto }) {
  const { t } = useTranslation();
  const facts = [
    [t('solutions.solutionKey'), version.solutionKey],
    [t('solutions.version'), version.solutionVersion],
    [t('solutions.packageHash'), version.packageSha256],
    [t('solutions.publisher'), version.publisherId],
    [t('solutions.publisherKey'), version.publisherKeyId],
    [t('solutions.openApiHash'), version.axisOpenApiSha256],
    [t('solutions.sourceRevision'), version.sourceRevision],
    [t('solutions.buildId'), version.buildId],
    [t('solutions.sourceUri'), version.sourceUri],
  ];
  return (
    <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
      {facts.map(([label, value]) => (
        <div key={label} className="min-w-0">
          <dt className="font-medium text-muted-foreground">{label}</dt>
          <dd className="break-all">{value || t('solutions.notAvailable')}</dd>
        </div>
      ))}
    </dl>
  );
}

function OperationPanel({
  operation,
  loading,
  error,
  resuming,
  onRetry,
  onResume,
}: {
  operation: SolutionOperationStatusDto | undefined;
  loading: boolean;
  error: boolean;
  resuming: boolean;
  onRetry: () => void;
  onResume: () => void;
}) {
  const { t } = useTranslation();
  if (loading) {
    return (
      <section aria-labelledby="operation-title">
        <h2 id="operation-title" className="text-lg font-medium">
          {t('solutions.operationTitle')}
        </h2>
        <AsyncContent pending pendingLabel={t('solutions.operationLoading')}>
          <span />
        </AsyncContent>
      </section>
    );
  }
  if (error || !operation) {
    return (
      <section aria-labelledby="operation-title">
        <h2 id="operation-title" className="sr-only">
          {t('solutions.operationTitle')}
        </h2>
        <StatusNotice tone="destructive" title={t('solutions.operationLoadFailed')}>
          <span>{t('solutions.operationLoadFailedDescription')}</span>{' '}
          <Button type="button" size="sm" variant="outline" onClick={onRetry}>
            {t('app.retry')}
          </Button>
        </StatusNotice>
      </section>
    );
  }
  const resumable = operation.status === 'Failed';
  return (
    <section className="grid gap-4 border-b border-border pb-5" aria-labelledby="operation-title">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 id="operation-title" className="text-lg font-medium">
            {t('solutions.operationTitle')}
          </h2>
          <p className="break-all text-sm text-muted-foreground">
            {operation.id ?? t('solutions.notAvailable')}
          </p>
        </div>
        <StatusBadge tone={operationTone(operation.status)}>
          {operationLabel(operation.status, t)}
        </StatusBadge>
      </div>
      {operation.problemCode ? (
        <StatusNotice tone="warning" title={t('solutions.operationNeedsAttention')}>
          {t('solutions.safeProblemCode', { code: operation.problemCode })}
        </StatusNotice>
      ) : null}
      <ComponentSequence components={operation.steps ?? []} />
      {resumable ? (
        <AsyncButton
          type="button"
          className="w-fit"
          disabled={resuming}
          onClick={onResume}
          icon={<Play aria-hidden />}
          pending={resuming}
          pendingLabel={t('solutions.resuming')}
        >
          {t('solutions.resumeAction')}
        </AsyncButton>
      ) : null}
    </section>
  );
}

function InstallationsPanel({
  installations,
  loading,
  error,
  onRetry,
  onOpenOperation,
}: {
  installations: SolutionInstallationStatusDto[];
  loading: boolean;
  error: boolean;
  onRetry: () => void;
  onOpenOperation: (operationId: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <section className="grid min-h-0 gap-4" aria-labelledby="installations-title">
      <div>
        <h2 id="installations-title" className="text-lg font-medium">
          {t('solutions.installationsTitle')}
        </h2>
        <p className="text-sm text-muted-foreground">{t('solutions.installationsDescription')}</p>
      </div>
      <AsyncContent
        pending={loading}
        error={error}
        pendingLabel={t('solutions.installationsLoading')}
      >
        {error ? (
          <StatusNotice tone="destructive" title={t('solutions.installationsLoadFailed')}>
            <span>{t('solutions.installationsLoadFailedDescription')}</span>{' '}
            <Button type="button" size="sm" variant="outline" onClick={onRetry}>
              <RotateCw aria-hidden />
              {t('app.retry')}
            </Button>
          </StatusNotice>
        ) : installations.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('solutions.installationsEmpty')}</p>
        ) : (
          <ul className="grid gap-4" aria-label={t('solutions.installationsListLabel')}>
            {installations.map((installation) => (
              <li
                key={installation.id}
                className="grid gap-3 border-b border-border pb-4 last:border-0"
              >
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="break-all font-medium">
                      {installation.solutionVersionId ?? t('solutions.notAvailable')}
                    </p>
                    <p className="break-all text-xs text-muted-foreground">
                      {installation.id ?? t('solutions.notAvailable')}
                    </p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <StatusBadge tone={provisioningTone(installation.provisioningStatus)}>
                      {provisioningLabel(installation.provisioningStatus, t)}
                    </StatusBadge>
                    <StatusBadge
                      tone={installation.complianceStatus === 'Compliant' ? 'success' : 'muted'}
                    >
                      {installation.complianceStatus === 'Compliant'
                        ? t('solutions.compliant')
                        : t('solutions.noncompliant')}
                    </StatusBadge>
                  </div>
                </div>
                <ComponentSequence components={installation.components ?? []} />
                {installation.operationId ? (
                  <Button
                    type="button"
                    className="w-fit"
                    size="sm"
                    variant="outline"
                    onClick={() => onOpenOperation(installation.operationId ?? '')}
                  >
                    {t('solutions.viewOperation')}
                  </Button>
                ) : null}
              </li>
            ))}
          </ul>
        )}
      </AsyncContent>
    </section>
  );
}

function ComponentPlan({ components }: { components: SolutionComponentPlanDto[] }) {
  const { t } = useTranslation();
  if (components.length === 0) {
    return <p className="text-sm text-muted-foreground">{t('solutions.componentPlanEmpty')}</p>;
  }
  return (
    <section className="grid gap-2" aria-labelledby="release-plan-title">
      <h3 id="release-plan-title" className="font-medium">
        {t('solutions.componentPlanLabel')}
      </h3>
      <ol className="grid gap-2">
        {components.map((component, index) => (
          <li
            key={`${component.type ?? 'component'}-${component.key ?? index}`}
            className="flex min-w-0 items-start gap-3 text-sm"
          >
            <span className="text-muted-foreground">{index + 1}.</span>
            <span className="min-w-0 flex-1">
              <span className="block break-all font-medium">
                {component.key ?? t('solutions.notAvailable')}
              </span>
              <span className="block break-all text-xs text-muted-foreground">
                {component.type ?? t('solutions.notAvailable')} ·{' '}
                {component.sha256 ?? t('solutions.notAvailable')}
              </span>
            </span>
          </li>
        ))}
      </ol>
    </section>
  );
}

function ComponentSequence({ components }: { components: SolutionComponentStatusDto[] }) {
  const { t } = useTranslation();
  if (components.length === 0) {
    return <p className="text-sm text-muted-foreground">{t('solutions.componentsEmpty')}</p>;
  }
  return (
    <ol className="grid gap-2" aria-label={t('solutions.componentPlanLabel')}>
      {components.map((component, index) => (
        <li
          key={`${component.type ?? 'component'}-${component.key ?? index}`}
          className="flex min-w-0 items-start gap-3 text-sm"
        >
          <span className="text-muted-foreground">{index + 1}.</span>
          <span className="min-w-0 flex-1">
            <span className="block break-all font-medium">
              {component.key ?? t('solutions.notAvailable')}
            </span>
            <span className="block break-all text-xs text-muted-foreground">
              {component.type ?? t('solutions.notAvailable')} ·{' '}
              {component.sha256 ?? t('solutions.notAvailable')}
            </span>
            {component.problemCode ? (
              <span className="block break-all text-xs text-muted-foreground">
                {t('solutions.safeProblemCode', { code: component.problemCode })}
              </span>
            ) : null}
          </span>
          <StatusBadge tone={stepTone(component.status)}>
            {stepLabel(component.status, t)}
          </StatusBadge>
        </li>
      ))}
    </ol>
  );
}

function createIdempotencyKey(): string {
  return globalThis.crypto.randomUUID();
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KiB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MiB`;
}

function solutionProblemFeedback(
  error: unknown,
  t: (key: string, values?: Record<string, unknown>) => string,
): Feedback {
  if (error instanceof ApiError && error.status === 413) {
    return {
      tone: 'warning',
      title: t('solutions.packageRejected'),
      body: t('solutions.packageTooLarge'),
    };
  }
  if (
    error instanceof ApiError &&
    (error.status === 400 || error.status === 415 || error.status === 422)
  ) {
    return {
      tone: 'warning',
      title: t('solutions.packageRejected'),
      body: t('solutions.packageRejectedDescription'),
    };
  }
  if (error instanceof ApiError && error.status === 409) {
    return {
      tone: 'warning',
      title: t('solutions.conflict'),
      body: t('solutions.conflictDescription'),
    };
  }
  if (error instanceof ApiError && error.status === 503) {
    return {
      tone: 'warning',
      title: t('solutions.unavailable'),
      body: t('solutions.unavailableDescription'),
    };
  }
  return {
    tone: 'destructive',
    title: t('solutions.actionFailed'),
    body: t('solutions.actionFailedDescription'),
  };
}

function publishProblemFeedback(
  error: unknown,
  t: (key: string, values?: Record<string, unknown>) => string,
): Feedback {
  if (isPublisherTrustProblem(error)) {
    return {
      tone: 'warning',
      title: t('solutions.publisherTrustUnavailable'),
      body: t('solutions.publisherTrustUnavailableDescription'),
      retryPublish: true,
    };
  }
  return solutionProblemFeedback(error, t);
}

function isPublisherTrustProblem(error: unknown): boolean {
  if (!(error instanceof ApiError) || typeof error.data !== 'object' || error.data === null) {
    return false;
  }
  return 'code' in error.data && error.data.code === 'solutions.package.publisher_untrusted';
}

function trustTone(status: string | undefined): StatusBadgeTone {
  return status === 'Trusted' ? 'success' : status === 'Unknown' ? 'neutral' : 'muted';
}

function operationTone(status: SolutionOperationStatus | undefined): StatusBadgeTone {
  if (status === 'Succeeded') return 'success';
  if (status === 'Pending' || status === 'Running') return 'info';
  return 'muted';
}

function provisioningTone(status: SolutionProvisioningStatus | undefined): StatusBadgeTone {
  if (status === 'Installed') return 'success';
  if (status === 'Installing') return 'info';
  return 'muted';
}

function stepTone(status: SolutionStepStatus | undefined): StatusBadgeTone {
  if (status === 'Confirmed') return 'success';
  if (status === 'Applying') return 'info';
  if (status === 'Pending') return 'neutral';
  return 'muted';
}

function solutionTrustLabel(status: string | undefined, t: (key: string) => string): string {
  if (status === 'Trusted') return t('solutions.trusted');
  if (status === 'Revoked') return t('solutions.revoked');
  return t('solutions.trustUnknown');
}

function operationLabel(
  status: SolutionOperationStatus | undefined,
  t: (key: string) => string,
): string {
  if (status === 'Pending') return t('solutions.operationPending');
  if (status === 'Running') return t('solutions.operationRunning');
  if (status === 'Succeeded') return t('solutions.operationSucceeded');
  if (status === 'Blocked') return t('solutions.operationBlocked');
  return t('solutions.operationFailed');
}

function provisioningLabel(
  status: SolutionProvisioningStatus | undefined,
  t: (key: string) => string,
): string {
  if (status === 'Installing') return t('solutions.installingStatus');
  if (status === 'Installed') return t('solutions.installedStatus');
  return t('solutions.failedStatus');
}

function stepLabel(status: SolutionStepStatus | undefined, t: (key: string) => string): string {
  if (status === 'Pending') return t('solutions.stepPending');
  if (status === 'Applying') return t('solutions.stepApplying');
  if (status === 'Confirmed') return t('solutions.stepConfirmed');
  return t('solutions.stepFailed');
}
