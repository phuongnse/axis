import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { PackageCheck, Play, RotateCw, Upload } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AsyncButton } from '@/components/shared/AsyncButton';
import { AsyncContent } from '@/components/shared/AsyncContent';
import {
  ManagedDialog,
  ManagedDialogAction,
  ManagedDialogAsyncAction,
  ManagedDialogBody,
} from '@/components/shared/ManagedDialog';
import {
  type ManagedWindowDescriptor,
  type ManagedWindowRendererProps,
  type ManagedWindowRendererRegistry,
  useCurrentManagedWindow,
} from '@/components/shared/ManagedWindowManager';
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
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { ApiError } from '@/lib/api';
import {
  installSolutionVersion,
  publishSolutionVersion,
  resumeSolutionOperation,
  solutionInstallationsQueryOptions,
  solutionOperationQueryOptions,
  solutionPackageMaxBytes,
  solutionQueryKeys,
  solutionVersionQueryOptions,
  solutionVersionsQueryOptions,
} from './api';
import {
  ComplianceStatusBadge,
  ComponentPlan,
  ComponentSequence,
  OperationStatusBadge,
  ProvisioningStatusBadge,
  SolutionTrustBadge,
  VersionFacts,
} from './components/SolutionPresentation';
import { findExistingSolutionInstallation } from './installation-availability';

const SOLUTION_PUBLISH_KIND = 'solutions.publish';
const SOLUTION_RELEASE_KIND = 'solutions.release';
const SOLUTION_INSTALLATION_KIND = 'solutions.installation';

type Feedback = {
  tone: StatusNoticeTone;
  title: string;
  body: string;
  retryPublish?: boolean;
};

export function solutionPublishWindowDescriptor(title: string): ManagedWindowDescriptor {
  return {
    id: 'solutions:publish',
    kind: SOLUTION_PUBLISH_KIND,
    resourceKey: 'publish',
    title,
  };
}

export function solutionReleaseWindowDescriptor(
  versionId: string,
  title: string,
): ManagedWindowDescriptor {
  return {
    id: `solutions:release:${versionId}`,
    kind: SOLUTION_RELEASE_KIND,
    resourceKey: versionId,
    title,
    payload: { versionId },
  };
}

export function solutionInstallationWindowDescriptor(
  installationId: string,
  title: string,
): ManagedWindowDescriptor {
  return {
    id: `solutions:installation:${installationId}`,
    kind: SOLUTION_INSTALLATION_KIND,
    resourceKey: installationId,
    title,
    payload: { installationId },
  };
}

export const solutionsManagedWindowRenderers: ManagedWindowRendererRegistry = {
  [SOLUTION_PUBLISH_KIND]: SolutionPublishWindowRenderer,
  [SOLUTION_RELEASE_KIND]: SolutionReleaseWindowRenderer,
  [SOLUTION_INSTALLATION_KIND]: SolutionInstallationWindowRenderer,
};

function SolutionPublishWindowRenderer() {
  const { windowId, closeWindow, openWindow } = useCurrentManagedWindow();
  return (
    <SolutionPublishDialog
      onClose={() => closeWindow(windowId)}
      onOpenRelease={(versionId, title) => {
        closeWindow(windowId);
        openWindow(solutionReleaseWindowDescriptor(versionId, title));
      }}
    />
  );
}

function SolutionReleaseWindowRenderer({ descriptor }: ManagedWindowRendererProps) {
  const { windowId, closeWindow, openWindow } = useCurrentManagedWindow();
  const versionId = readPayloadId(descriptor, 'versionId');
  return (
    <SolutionReleaseDialog
      versionId={versionId}
      fallbackTitle={descriptor.title}
      onClose={() => closeWindow(windowId)}
      onOpenInstallation={(installationId, title) =>
        openWindow(solutionInstallationWindowDescriptor(installationId, title))
      }
    />
  );
}

function SolutionInstallationWindowRenderer({ descriptor }: ManagedWindowRendererProps) {
  const { windowId, closeWindow } = useCurrentManagedWindow();
  return (
    <SolutionInstallationDialog
      installationId={readPayloadId(descriptor, 'installationId')}
      fallbackTitle={descriptor.title}
      onClose={() => closeWindow(windowId)}
    />
  );
}

function SolutionPublishDialog({
  onClose,
  onOpenRelease,
}: {
  onClose: () => void;
  onOpenRelease: (versionId: string, title: string) => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [fileInputKey, setFileInputKey] = useState(0);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [discardOpen, setDiscardOpen] = useState(false);
  const [publishedVersion, setPublishedVersion] =
    useState<Awaited<ReturnType<typeof publishSolutionVersion>>['version']>();
  const dirty = Boolean(file);

  const mutation = useMutation({
    mutationFn: publishSolutionVersion,
    onSuccess: async (result) => {
      setPublishedVersion(result.version);
      setFile(null);
      setFileError(null);
      setFileInputKey((current) => current + 1);
      setFeedback({
        tone: 'success',
        title: t('solutions.publishSucceeded'),
        body: result.isRetry
          ? t('solutions.publishCanonicalRetry')
          : t('solutions.publishSucceededDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: solutionQueryKeys.versions() });
    },
    onError: (error) => setFeedback(publishProblemFeedback(error, t)),
  });

  function selectFile(selected: File | null) {
    setFeedback(null);
    setPublishedVersion(undefined);
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

  function requestClose() {
    if (dirty) setDiscardOpen(true);
    else onClose();
  }

  const releaseTitle = publishedVersion
    ? `${publishedVersion.solutionKey ?? t('solutions.releaseTitle')} ${publishedVersion.solutionVersion ?? ''}`.trim()
    : t('solutions.releaseTitle');

  return (
    <>
      <ManagedDialog
        surfaceId="solution-delivery-windows"
        open
        title={t('solutions.publishTitle')}
        description={t('solutions.publishDescription')}
        dirty={dirty}
        closeDisabled={mutation.isPending}
        onOpenChange={(open) => {
          if (!open) requestClose();
        }}
        footer={
          <>
            <ManagedDialogAction
              type="button"
              variant="outline"
              disabled={mutation.isPending}
              onClick={requestClose}
            >
              {publishedVersion ? t('app.close') : t('app.cancel')}
            </ManagedDialogAction>
            {publishedVersion?.id ? (
              <ManagedDialogAction
                type="button"
                onClick={() => onOpenRelease(publishedVersion.id ?? '', releaseTitle)}
              >
                {t('solutions.viewRelease')}
              </ManagedDialogAction>
            ) : (
              <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
                <AlertDialogTrigger
                  render={
                    <ManagedDialogAsyncAction
                      type="button"
                      disabled={!file || mutation.isPending}
                      pending={mutation.isPending}
                      pendingLabel={t('solutions.publishing')}
                      icon={<Upload aria-hidden />}
                    >
                      {t('solutions.publishAction')}
                    </ManagedDialogAsyncAction>
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
                          setConfirmOpen(false);
                          mutation.mutate(file);
                        }
                      }}
                    >
                      {t('solutions.publishAction')}
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            )}
          </>
        }
      >
        <ManagedDialogBody className="grid content-start gap-4">
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
                      disabled={mutation.isPending}
                      onClick={() => mutation.mutate(file)}
                      icon={<RotateCw aria-hidden />}
                      pending={mutation.isPending}
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
          {publishedVersion ? (
            <>
              <VersionFacts version={publishedVersion} />
              <ComponentPlan components={publishedVersion.components ?? []} />
            </>
          ) : (
            <Field data-invalid={Boolean(fileError)}>
              <FieldLabel htmlFor="solution-package">{t('solutions.packageLabel')}</FieldLabel>
              <Input
                key={fileInputKey}
                id="solution-package"
                type="file"
                accept="application/vnd.dsse.envelope.v1+json,.axis-solution"
                disabled={mutation.isPending}
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
              {file ? (
                <p className="text-sm" aria-live="polite">
                  {t('solutions.packageSelected', {
                    name: file.name,
                    size: formatBytes(file.size),
                  })}
                </p>
              ) : null}
            </Field>
          )}
        </ManagedDialogBody>
      </ManagedDialog>
      <AlertDialog open={discardOpen} onOpenChange={setDiscardOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('solutions.discardPublishTitle')}</AlertDialogTitle>
            <AlertDialogDescription>
              {t('solutions.discardPublishDescription')}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('solutions.keepPublishing')}</AlertDialogCancel>
            <AlertDialogAction onClick={onClose}>{t('solutions.discardPublish')}</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}

function SolutionReleaseDialog({
  versionId,
  fallbackTitle,
  onClose,
  onOpenInstallation,
}: {
  versionId: string;
  fallbackTitle: string;
  onClose: () => void;
  onOpenInstallation: (installationId: string, title: string) => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [installKey] = useState(createIdempotencyKey);
  const versionQuery = useQuery(solutionVersionQueryOptions(versionId));
  const versionsQuery = useQuery(solutionVersionsQueryOptions());
  const installationsQuery = useQuery(solutionInstallationsQueryOptions());
  const version = versionQuery.data;
  const existingInstallation = version
    ? findExistingSolutionInstallation(
        version,
        versionsQuery.data ?? [],
        installationsQuery.data ?? [],
      )
    : undefined;
  const availabilityPending = versionsQuery.isPending || installationsQuery.isPending;
  const availabilityError = versionsQuery.isError || installationsQuery.isError;
  const installable =
    !availabilityPending &&
    !availabilityError &&
    version?.id !== undefined &&
    version.trustStatus === 'Trusted' &&
    !existingInstallation;
  const mutation = useMutation({
    mutationFn: () => installSolutionVersion(versionId, installKey),
    onSuccess: async (result) => {
      const operation = result.operation;
      if (operation?.id) {
        queryClient.setQueryData(solutionQueryKeys.operation(operation.id), operation);
      }
      setFeedback({
        tone: 'success',
        title: t('solutions.installStarted'),
        body: result.isRetry
          ? t('solutions.installCanonicalRetry')
          : t('solutions.installStartedDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: solutionQueryKeys.installations() });
      if (operation?.installationId) {
        onOpenInstallation(operation.installationId, t('solutions.operationTitle'));
      }
    },
    onError: (error) => setFeedback(solutionProblemFeedback(error, t)),
  });
  const title = version
    ? `${version.solutionKey ?? t('solutions.releaseTitle')} ${version.solutionVersion ?? ''}`.trim()
    : fallbackTitle;

  return (
    <ManagedDialog
      surfaceId="solution-delivery-windows"
      open
      title={title}
      description={t('solutions.releaseDescription')}
      titleAccessory={version ? <SolutionTrustBadge status={version.trustStatus} /> : undefined}
      closeDisabled={mutation.isPending}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      footer={
        <>
          <ManagedDialogAction
            type="button"
            variant="outline"
            disabled={mutation.isPending}
            onClick={onClose}
          >
            {t('app.close')}
          </ManagedDialogAction>
          {installable ? (
            <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
              <AlertDialogTrigger
                render={
                  <ManagedDialogAsyncAction
                    type="button"
                    disabled={mutation.isPending}
                    pending={mutation.isPending}
                    pendingLabel={t('solutions.installing')}
                    icon={<PackageCheck aria-hidden />}
                  >
                    {t('solutions.installAction')}
                  </ManagedDialogAsyncAction>
                }
              />
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>{t('solutions.installConfirmTitle')}</AlertDialogTitle>
                  <AlertDialogDescription>
                    {t('solutions.installConfirmDescription', {
                      solution: version?.solutionKey ?? t('solutions.notAvailable'),
                      version: version?.solutionVersion ?? t('solutions.notAvailable'),
                    })}
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>{t('app.cancel')}</AlertDialogCancel>
                  <AlertDialogAction
                    onClick={() => {
                      setConfirmOpen(false);
                      mutation.mutate();
                    }}
                  >
                    {t('solutions.installAction')}
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          ) : null}
        </>
      }
    >
      <ManagedDialogBody className="grid content-start gap-4">
        {feedback ? (
          <div aria-live="polite">
            <StatusNotice tone={feedback.tone} title={feedback.title}>
              {feedback.body}
            </StatusNotice>
          </div>
        ) : null}
        <AsyncContent
          pending={versionQuery.isPending || availabilityPending}
          error={versionQuery.isError || availabilityError}
          pendingLabel={t('solutions.releaseLoading')}
        >
          {versionQuery.isError || availabilityError || !version ? (
            <StatusNotice tone="destructive" title={t('solutions.releaseLoadFailed')}>
              <ManagedDialogAction
                type="button"
                variant="outline"
                onClick={() =>
                  void Promise.all([
                    versionQuery.refetch(),
                    versionsQuery.refetch(),
                    installationsQuery.refetch(),
                  ])
                }
              >
                {t('app.retry')}
              </ManagedDialogAction>
            </StatusNotice>
          ) : (
            <div className="grid gap-5">
              <VersionFacts version={version} />
              <ComponentPlan components={version.components ?? []} />
              {!installable ? (
                <StatusNotice tone="warning" title={t('solutions.installUnavailable')}>
                  {existingInstallation
                    ? t('solutions.installUnavailableExistingDescription', {
                        version:
                          existingInstallation.installedVersion.solutionVersion ??
                          t('solutions.notAvailable'),
                      })
                    : t('solutions.installUnavailableDescription')}
                </StatusNotice>
              ) : null}
            </div>
          )}
        </AsyncContent>
      </ManagedDialogBody>
    </ManagedDialog>
  );
}

function SolutionInstallationDialog({
  installationId,
  fallbackTitle,
  onClose,
}: {
  installationId: string;
  fallbackTitle: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const installationsQuery = useQuery(solutionInstallationsQueryOptions());
  const versionsQuery = useQuery(solutionVersionsQueryOptions());
  const installation = installationsQuery.data?.find(
    (candidate) => candidate.id === installationId,
  );
  const version = versionsQuery.data?.find(
    (candidate) => candidate.id === installation?.solutionVersionId,
  );
  const operationId = installation?.operationId ?? '';
  const operationQuery = useQuery(solutionOperationQueryOptions(operationId));
  const operation = operationQuery.data;
  const resumeMutation = useMutation({
    mutationFn: () => resumeSolutionOperation(operationId),
    onSuccess: async (result) => {
      if (result.id) queryClient.setQueryData(solutionQueryKeys.operation(result.id), result);
      setFeedback({
        tone: 'info',
        title: t('solutions.resumeAccepted'),
        body: t('solutions.resumeAcceptedDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: solutionQueryKeys.installations() });
    },
    onError: (error) => setFeedback(solutionProblemFeedback(error, t)),
  });

  useEffect(() => {
    if (operation?.status === 'Succeeded' || operation?.status === 'Failed') {
      void queryClient.invalidateQueries({ queryKey: solutionQueryKeys.installations() });
    }
  }, [operation?.status, queryClient]);

  const title = version
    ? t('solutions.installationWindowTitle', {
        solution: version.solutionKey ?? fallbackTitle,
        version: version.solutionVersion ?? '',
      })
    : fallbackTitle;
  const loading =
    installationsQuery.isPending || (Boolean(operationId) && operationQuery.isPending);
  const error = installationsQuery.isError || (Boolean(operationId) && operationQuery.isError);
  const components = operation?.steps ?? installation?.components ?? [];

  return (
    <ManagedDialog
      surfaceId="solution-delivery-windows"
      open
      title={title}
      description={t('solutions.installationDetailsDescription')}
      titleAccessory={
        operation ? (
          <OperationStatusBadge status={operation.status} />
        ) : installation ? (
          <ProvisioningStatusBadge status={installation.provisioningStatus} />
        ) : undefined
      }
      closeDisabled={resumeMutation.isPending}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      footer={
        <>
          <ManagedDialogAction
            type="button"
            variant="outline"
            disabled={resumeMutation.isPending}
            onClick={onClose}
          >
            {t('app.close')}
          </ManagedDialogAction>
          {operation?.status === 'Failed' ? (
            <ManagedDialogAsyncAction
              type="button"
              disabled={resumeMutation.isPending}
              onClick={() => resumeMutation.mutate()}
              icon={<Play aria-hidden />}
              pending={resumeMutation.isPending}
              pendingLabel={t('solutions.resuming')}
            >
              {t('solutions.resumeAction')}
            </ManagedDialogAsyncAction>
          ) : null}
        </>
      }
    >
      <ManagedDialogBody className="grid content-start gap-4">
        {feedback ? (
          <div aria-live="polite">
            <StatusNotice tone={feedback.tone} title={feedback.title}>
              {feedback.body}
            </StatusNotice>
          </div>
        ) : null}
        <AsyncContent
          pending={loading}
          error={error}
          pendingLabel={t('solutions.operationLoading')}
        >
          {error || !installation ? (
            <StatusNotice tone="destructive" title={t('solutions.operationLoadFailed')}>
              <span>{t('solutions.operationLoadFailedDescription')}</span>{' '}
              <ManagedDialogAction
                type="button"
                variant="outline"
                onClick={() => {
                  void installationsQuery.refetch();
                  if (operationId) void operationQuery.refetch();
                }}
              >
                {t('app.retry')}
              </ManagedDialogAction>
            </StatusNotice>
          ) : (
            <div className="grid gap-5">
              <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
                <div>
                  <dt className="font-medium text-muted-foreground">
                    {t('solutions.installationId')}
                  </dt>
                  <dd className="break-all">{installation.id ?? t('solutions.notAvailable')}</dd>
                </div>
                <div>
                  <dt className="font-medium text-muted-foreground">
                    {t('solutions.operationId')}
                  </dt>
                  <dd className="break-all">
                    {installation.operationId ?? t('solutions.notAvailable')}
                  </dd>
                </div>
              </dl>
              <div className="flex flex-wrap gap-2">
                <ProvisioningStatusBadge status={installation.provisioningStatus} />
                <ComplianceStatusBadge status={installation.complianceStatus} />
              </div>
              {operation?.problemCode ? (
                <StatusNotice tone="warning" title={t('solutions.operationNeedsAttention')}>
                  {t('solutions.safeProblemCode', { code: operation.problemCode })}
                </StatusNotice>
              ) : null}
              <ComponentSequence components={components} />
            </div>
          )}
        </AsyncContent>
      </ManagedDialogBody>
    </ManagedDialog>
  );
}

function readPayloadId(descriptor: ManagedWindowDescriptor, key: string): string {
  if (!descriptor.payload || typeof descriptor.payload !== 'object') return '';
  const value = (descriptor.payload as Record<string, unknown>)[key];
  return typeof value === 'string' ? value : '';
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
