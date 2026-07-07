import { zodResolver } from '@hookform/resolvers/zod';
import { type QueryClient, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  AlertCircle,
  ArrowDown,
  ArrowUp,
  CheckCircle2,
  FilePlus2,
  LockKeyhole,
  Maximize2,
  Minimize2,
  Plus,
  Save,
  Trash2,
  UploadCloud,
} from 'lucide-react';
import { type ReactNode, useEffect, useState } from 'react';
import { type UseFormSetError, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardFooter, CardHeader, CardTitle } from '@/components/ui/card';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { ApiError } from '@/lib/api';
import { cn } from '@/lib/utils';
import {
  createObjectDefinitionDraft,
  type ObjectDefinitionDetail,
  type ObjectFieldDefinitionInput,
  objectDefinitionDetailQueryOptions,
  objectDefinitionQueryKeys,
  objectDefinitionsDefaultPageSize,
  objectDefinitionsListQueryOptions,
  publishObjectDefinition,
  saveObjectDefinitionDraft,
} from '../api';

const keyPattern = /^[a-z][a-z0-9_]{0,62}$/;

const identitySchema = z.object({
  name: z.string().trim().min(1, 'objects.validationName'),
});

type IdentityFormValues = z.infer<typeof identitySchema>;

interface EditableField {
  clientId: string;
  fieldKey: string;
  label: string;
}

type FeedbackScope = 'identity' | 'fields' | 'publish';

interface FeedbackState {
  scope: FeedbackScope;
  variant: 'success' | 'destructive';
  titleKey: string;
  detail?: string;
}

interface EditableFieldErrors {
  fieldKey?: string;
  label?: string;
}

interface FieldValidationResult {
  messages: string[];
  fieldErrors: Record<string, EditableFieldErrors>;
}

export function BusinessObjectsPage() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [selectedDefinition, setSelectedDefinition] = useState<ObjectDefinitionDetail | null>(null);
  const [fields, setFields] = useState<EditableField[]>([]);
  const [feedback, setFeedback] = useState<FeedbackState | null>(null);
  const [fieldValidationMode, setFieldValidationMode] = useState<'save' | 'publish' | null>(null);
  const [fieldsExpanded, setFieldsExpanded] = useState(false);

  const identityForm = useForm<IdentityFormValues>({
    resolver: zodResolver(identitySchema),
    defaultValues: { name: '' },
  });

  const listQuery = useQuery(objectDefinitionsListQueryOptions(page));
  const detailQuery = useQuery({
    ...objectDefinitionDetailQueryOptions(selectedId ?? ''),
    enabled: Boolean(selectedId),
  });

  const createMutation = useMutation({
    mutationFn: createObjectDefinitionDraft,
    onSuccess: (definition) => {
      setSelectedId(definition.id ?? null);
      setSelectedDefinition(definition);
      setFields(fromDefinitionFields(definition));
      identityForm.reset({
        name: definition.name ?? '',
      });
      setFeedback({ scope: 'identity', variant: 'success', titleKey: 'objects.created' });
      setFieldValidationMode(null);
      cacheObjectDefinitionResult(queryClient, definition);
    },
    onError: (error) => {
      applyIdentityApiErrors(error, identityForm.setError);
      setFeedback(apiErrorFeedback(error, 'identity'));
    },
  });

  const saveMutation = useMutation({
    mutationFn: ({
      id,
      request,
    }: {
      id: string;
      request: Parameters<typeof saveObjectDefinitionDraft>[1];
    }) => saveObjectDefinitionDraft(id, request),
    onSuccess: (definition) => {
      setSelectedId(definition.id ?? null);
      setSelectedDefinition(definition);
      setFields(fromDefinitionFields(definition));
      setFeedback({ scope: 'fields', variant: 'success', titleKey: 'objects.saved' });
      setFieldValidationMode(null);
      cacheObjectDefinitionResult(queryClient, definition);
    },
    onError: (error) => {
      const hasIdentityError = applyIdentityApiErrors(error, identityForm.setError);
      setFeedback(apiErrorFeedback(error, hasIdentityError ? 'identity' : 'fields'));
    },
  });

  const publishMutation = useMutation({
    mutationFn: ({ id, expectedDraftVersion }: { id: string; expectedDraftVersion: number }) =>
      publishObjectDefinition(id, { expectedDraftVersion }),
    onSuccess: (definition) => {
      setSelectedId(definition.id ?? null);
      setSelectedDefinition(definition);
      setFields(fromDefinitionFields(definition));
      setFeedback({ scope: 'publish', variant: 'success', titleKey: 'objects.published' });
      setFieldValidationMode(null);
      cacheObjectDefinitionResult(queryClient, definition);
    },
    onError: (error) => setFeedback(apiErrorFeedback(error, 'publish')),
  });

  useEffect(() => {
    if (!selectedDefinition) return;
    identityForm.reset({
      name: selectedDefinition.name ?? '',
    });
  }, [identityForm, selectedDefinition]);

  useEffect(() => {
    if (!detailQuery.data) return;

    setSelectedDefinition(detailQuery.data);
    setFields(fromDefinitionFields(detailQuery.data));
    identityForm.reset({
      name: detailQuery.data.name ?? '',
    });
  }, [detailQuery.data, identityForm]);

  const totalCount = listQuery.data?.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / objectDefinitionsDefaultPageSize));
  const selectedStatus = selectedDefinition?.status ?? 'Draft';
  const isPublished = selectedStatus === 'Published';
  const busy = createMutation.isPending || saveMutation.isPending || publishMutation.isPending;
  const addFieldUnavailable = !selectedDefinition || isPublished;
  const editorSubmitUnavailable = busy || isPublished;
  const publishValidation = selectedDefinition
    ? validateFields(fields, t, { requireFields: true })
    : emptyFieldValidationResult();
  const publishUnavailable =
    !selectedDefinition || busy || isPublished || publishValidation.messages.length > 0;
  const activeFieldValidation = fieldValidationMode
    ? validateFields(fields, t, { requireFields: fieldValidationMode === 'publish' })
    : emptyFieldValidationResult();
  const identityFeedback = feedback?.scope === 'identity' ? feedback : null;
  const fieldsFeedback =
    feedback?.scope === 'fields'
      ? currentValidationFeedback(feedback, fieldValidationMode === 'save', activeFieldValidation)
      : null;
  const publishFeedback =
    !isPublished && feedback?.scope === 'publish'
      ? currentValidationFeedback(
          feedback,
          fieldValidationMode === 'publish',
          activeFieldValidation,
        )
      : null;
  const objectKeyPreview =
    selectedDefinition?.objectKey ?? deriveObjectKey(identityForm.watch('name'));

  async function handleCreate(values: IdentityFormValues) {
    setFeedback(null);
    identityForm.clearErrors();
    createMutation.mutate({
      name: values.name,
    });
  }

  function handleNewDraft() {
    setSelectedDefinition(null);
    setSelectedId(null);
    setFields([]);
    setFeedback(null);
    setFieldValidationMode(null);
    setFieldsExpanded(false);
    identityForm.reset({ name: '' });
  }

  function handleSelect(id: string | undefined) {
    if (!id) return;
    setSelectedId(id);
    setFeedback(null);
    setFieldValidationMode(null);
  }

  function prefetchDefinition(id: string | undefined) {
    if (!id) return;
    void queryClient.prefetchQuery(objectDefinitionDetailQueryOptions(id));
  }

  function prefetchPage(nextPage: number) {
    if (nextPage < 1 || nextPage > totalPages || nextPage === page) return;
    void queryClient.prefetchQuery(objectDefinitionsListQueryOptions(nextPage));
  }

  function addField() {
    setFields((current) => [
      ...current,
      {
        clientId: crypto.randomUUID(),
        fieldKey: '',
        label: '',
      },
    ]);
  }

  function updateField(clientId: string, patch: Partial<EditableField>) {
    setFields((current) =>
      current.map((field) => (field.clientId === clientId ? { ...field, ...patch } : field)),
    );
  }

  function removeField(clientId: string) {
    setFields((current) => current.filter((field) => field.clientId !== clientId));
  }

  function moveField(clientId: string, direction: -1 | 1) {
    setFields((current) => {
      const index = current.findIndex((field) => field.clientId === clientId);
      const nextIndex = index + direction;
      if (index < 0 || nextIndex < 0 || nextIndex >= current.length) return current;

      const next = [...current];
      [next[index], next[nextIndex]] = [next[nextIndex], next[index]];
      return next;
    });
  }

  async function handleSave(values: IdentityFormValues) {
    if (!selectedDefinition?.id || !selectedDefinition.draftVersion) return;

    const validation = validateFields(fields, t, { requireFields: false });
    setFieldValidationMode('save');
    if (validation.messages.length > 0) {
      setFeedback({
        scope: 'fields',
        variant: 'destructive',
        titleKey: 'objects.validationTitle',
        detail: validation.messages.join(' '),
      });
      return;
    }

    setFeedback(null);
    setFieldValidationMode(null);
    saveMutation.mutate({
      id: selectedDefinition.id,
      request: {
        expectedDraftVersion: selectedDefinition.draftVersion,
        name: values.name,
        fields: fields.map(toFieldInput),
      },
    });
  }

  function handlePublish() {
    if (!selectedDefinition?.id || !selectedDefinition.draftVersion) return;

    const validation = validateFields(fields, t, { requireFields: true });
    setFieldValidationMode('publish');
    if (validation.messages.length > 0) {
      setFeedback({
        scope: 'publish',
        variant: 'destructive',
        titleKey: 'objects.validationTitle',
        detail: validation.messages.join(' '),
      });
      return;
    }

    setFeedback(null);
    setFieldValidationMode(null);
    publishMutation.mutate({
      id: selectedDefinition.id,
      expectedDraftVersion: selectedDefinition.draftVersion,
    });
  }

  return (
    <div
      className={cn(
        'flex h-full min-h-0 w-full min-w-0 flex-col gap-4 overflow-x-hidden',
        fieldsExpanded ? 'overflow-hidden' : 'overflow-y-auto 2xl:overflow-hidden',
      )}
    >
      <div className="flex min-w-0 shrink-0 flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold text-foreground">{t('objects.title')}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t('objects.pageDescription')}</p>
        </div>
        <Button type="button" onClick={handleNewDraft}>
          <FilePlus2 className="size-4" aria-hidden />
          {t('objects.new')}
        </Button>
      </div>

      <div
        className={cn(
          'grid min-w-0 gap-4',
          fieldsExpanded
            ? 'min-h-0 flex-1 grid-cols-1 overflow-hidden'
            : 'shrink-0 lg:grid-cols-[minmax(16rem,20rem)_minmax(0,1fr)] 2xl:min-h-0 2xl:flex-1 2xl:grid-cols-[minmax(16rem,20rem)_minmax(0,1fr)_minmax(17rem,20rem)] 2xl:overflow-hidden',
        )}
      >
        {fieldsExpanded ? null : (
          <Card
            size="sm"
            role="region"
            aria-labelledby="objects-list-title"
            className="min-h-0 min-w-0 gap-0 py-0"
          >
            <div className="flex items-center justify-between border-b border-border px-4 py-3">
              <div className="min-w-0">
                <h2 id="objects-list-title" className="text-sm font-semibold">
                  {t('objects.listTitle')}
                </h2>
                <p className="mt-1 text-xs text-muted-foreground">
                  {t('objects.definitionCount', { count: totalCount })}
                </p>
              </div>
            </div>

            <div className="min-h-64 flex-1 overflow-y-auto p-2 2xl:min-h-0">
              {listQuery.isLoading ? (
                <p className="px-2 py-3 text-sm text-muted-foreground">{t('app.saving')}</p>
              ) : null}
              {listQuery.isError ? (
                <Alert variant="destructive">
                  <AlertCircle className="size-4" aria-hidden />
                  <AlertTitle>{t('objects.loadError')}</AlertTitle>
                </Alert>
              ) : null}
              {!listQuery.isLoading && !listQuery.isError && totalCount === 0 ? (
                <div className="rounded-md border border-dashed border-border px-3 py-4">
                  <p className="text-sm font-medium text-foreground">{t('objects.emptyTitle')}</p>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t('objects.emptyDescription')}
                  </p>
                </div>
              ) : null}
              <div className="flex flex-col gap-1">
                {(listQuery.data?.items ?? []).map((definition) => {
                  const active =
                    definition.id === selectedId || definition.id === selectedDefinition?.id;
                  return (
                    <Button
                      key={definition.id}
                      type="button"
                      variant="outline"
                      onFocus={() => prefetchDefinition(definition.id)}
                      onMouseEnter={() => prefetchDefinition(definition.id)}
                      onClick={() => handleSelect(definition.id)}
                      className={cn(
                        'h-auto w-full min-w-0 justify-start rounded-md border-transparent bg-transparent px-2.5 py-2 text-left shadow-none hover:bg-accent focus-visible:ring-3 focus-visible:ring-ring/35',
                        active && 'bg-accent',
                      )}
                    >
                      <span className="flex min-w-0 items-center justify-between gap-2">
                        <span className="truncate text-sm font-medium text-foreground">
                          {definition.name}
                        </span>
                        {definition.latestPublishedVersionNumber ? (
                          <span className="shrink-0 text-xs text-muted-foreground">
                            {t('objects.latestVersion', {
                              version: definition.latestPublishedVersionNumber,
                            })}
                          </span>
                        ) : null}
                      </span>
                      <span className="mt-1 flex items-center gap-2 text-xs text-muted-foreground">
                        <span className="truncate">{definition.objectKey}</span>
                        <StatusBadge status={definition.status ?? 'Draft'} />
                      </span>
                    </Button>
                  );
                })}
              </div>
            </div>

            <div className="flex items-center justify-between border-t border-border px-4 py-3 text-xs text-muted-foreground">
              <span>
                {page} / {totalPages}
              </span>
              <div className="flex gap-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={page <= 1}
                  onFocus={() => prefetchPage(page - 1)}
                  onMouseEnter={() => prefetchPage(page - 1)}
                  onClick={() => setPage((current) => Math.max(1, current - 1))}
                >
                  <ArrowUp className="size-3.5" aria-hidden />
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={page >= totalPages}
                  onFocus={() => prefetchPage(page + 1)}
                  onMouseEnter={() => prefetchPage(page + 1)}
                  onClick={() => setPage((current) => Math.min(totalPages, current + 1))}
                >
                  <ArrowDown className="size-3.5" aria-hidden />
                </Button>
              </div>
            </div>
          </Card>
        )}

        <Card
          size="sm"
          aria-labelledby="objects-editor-title"
          className={cn('min-h-0 min-w-0 gap-0 py-0', fieldsExpanded && 'h-full')}
        >
          <form
            id="objects-definition-form"
            aria-labelledby="objects-editor-title"
            className="flex h-full min-h-0 min-w-0 flex-col"
            onSubmit={identityForm.handleSubmit(selectedDefinition ? handleSave : handleCreate)}
          >
            <div className="flex shrink-0 flex-col gap-3 border-b border-border px-4 py-4 sm:flex-row sm:items-start sm:justify-between">
              <div className="min-w-0">
                <h2 id="objects-editor-title" className="text-sm font-semibold">
                  {selectedDefinition ? selectedDefinition.name : t('objects.defineTitle')}
                </h2>
                <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
                  {selectedDefinition
                    ? t('objects.editorDescription')
                    : t('objects.defineDescription')}
                </p>
              </div>
            </div>

            {!fieldsExpanded && identityFeedback ? (
              <div className="shrink-0 px-4 pt-4">
                <FeedbackAlert feedback={identityFeedback} />
              </div>
            ) : null}

            {fieldsExpanded ? null : (
              <div className="grid shrink-0 gap-4 p-4 sm:grid-cols-2">
                <Field data-invalid={Boolean(identityForm.formState.errors.name)}>
                  <FieldLabel htmlFor="object-name">{t('objects.name')}</FieldLabel>
                  <Input
                    id="object-name"
                    aria-invalid={Boolean(identityForm.formState.errors.name)}
                    disabled={isPublished}
                    {...identityForm.register('name')}
                  />
                  <FieldError>
                    {identityForm.formState.errors.name?.message
                      ? t(identityForm.formState.errors.name.message)
                      : null}
                  </FieldError>
                  <FieldDescription>{t('objects.nameHelp')}</FieldDescription>
                </Field>
                <Field>
                  <FieldLabel htmlFor="object-key">{t('objects.objectKey')}</FieldLabel>
                  <div className="group/locked-control relative">
                    <Input
                      id="object-key"
                      value={objectKeyPreview}
                      readOnly
                      aria-readonly="true"
                      className="pr-8"
                    />
                    <LockedControlIndicator />
                  </div>
                  <FieldDescription>{t('objects.objectKeyHelp')}</FieldDescription>
                </Field>
              </div>
            )}

            <section
              aria-labelledby="objects-fields-title"
              className={cn(
                'flex flex-1 flex-col border-t border-border px-4 py-4',
                fieldsExpanded ? 'min-h-0' : 'min-h-56 2xl:min-h-0',
              )}
            >
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0 flex-1">
                  <h3 id="objects-fields-title" className="text-sm font-semibold">
                    {t('objects.fields')}
                  </h3>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {selectedDefinition ? t('objects.fieldsDescription') : t('objects.fieldsHelp')}
                  </p>
                </div>
                {selectedDefinition ? (
                  <FieldsExpansionButton
                    expanded={fieldsExpanded}
                    label={
                      fieldsExpanded
                        ? t('objects.restoreFieldsLayout')
                        : t('objects.expandFieldsEditor')
                    }
                    onToggle={() => setFieldsExpanded((current) => !current)}
                  />
                ) : null}
              </div>

              <div className="mt-3 flex justify-start">
                <DisabledActionHint
                  active={addFieldUnavailable}
                  label={t('objects.actionUnavailable')}
                >
                  <Button
                    type="button"
                    variant={addFieldUnavailable ? 'outline' : 'default'}
                    onClick={addField}
                    disabled={addFieldUnavailable}
                  >
                    <Plus className="size-4" aria-hidden />
                    {t('objects.addField')}
                  </Button>
                </DisabledActionHint>
              </div>

              {fieldsFeedback ? (
                <div className="mt-3">
                  <FeedbackAlert feedback={fieldsFeedback} />
                </div>
              ) : null}

              <div className="mt-4 flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto pr-1">
                {fields.map((field, index) => (
                  <FieldEditor
                    key={field.clientId}
                    field={field}
                    index={index}
                    disabled={isPublished}
                    onChange={(patch) => updateField(field.clientId, patch)}
                    onMove={moveField}
                    onRemove={removeField}
                    errors={activeFieldValidation.fieldErrors[field.clientId]}
                  />
                ))}
                {fields.length === 0 ? (
                  <div className="flex w-full flex-col self-start rounded-lg border border-dashed border-border px-3 py-3 text-sm">
                    <p className="font-medium text-foreground">
                      {selectedDefinition
                        ? t('objects.noFieldsTitle')
                        : t('objects.fieldsLockedTitle')}
                    </p>
                    <p className="mt-1 max-w-xl text-muted-foreground">
                      {selectedDefinition
                        ? t('objects.noFieldsDescription')
                        : t('objects.fieldsLockedDescription')}
                    </p>
                  </div>
                ) : null}
              </div>
            </section>

            {!isPublished ? (
              <CardFooter className="flex shrink-0 flex-col items-stretch gap-3">
                {publishFeedback ? <FeedbackAlert feedback={publishFeedback} /> : null}
                <div className="flex shrink-0 flex-col gap-2 sm:flex-row sm:justify-end">
                  <DisabledActionHint
                    active={editorSubmitUnavailable}
                    label={t('objects.actionUnavailable')}
                  >
                    <Button
                      type="submit"
                      form="objects-definition-form"
                      variant={editorSubmitUnavailable ? 'outline' : 'default'}
                      disabled={editorSubmitUnavailable}
                      className="w-full sm:w-auto"
                    >
                      {selectedDefinition ? (
                        <Save className="size-4" aria-hidden />
                      ) : (
                        <Plus className="size-4" aria-hidden />
                      )}
                      {selectedDefinition
                        ? saveMutation.isPending
                          ? t('objects.saving')
                          : t('objects.saveDraft')
                        : createMutation.isPending
                          ? t('objects.creating')
                          : t('objects.create')}
                    </Button>
                  </DisabledActionHint>
                  {selectedDefinition ? (
                    <DisabledActionHint
                      active={publishUnavailable}
                      label={t('objects.actionUnavailable')}
                    >
                      <Button
                        type="button"
                        variant={publishUnavailable ? 'outline' : 'default'}
                        disabled={publishUnavailable}
                        onClick={handlePublish}
                        className="w-full sm:w-auto"
                      >
                        <UploadCloud className="size-4" aria-hidden />
                        {publishMutation.isPending ? t('objects.publishing') : t('objects.publish')}
                      </Button>
                    </DisabledActionHint>
                  ) : null}
                </div>
              </CardFooter>
            ) : null}
          </form>
        </Card>

        {fieldsExpanded ? null : (
          <aside
            aria-label={t('objects.workflowLabel')}
            className="grid min-h-0 min-w-0 content-start gap-4 overflow-visible pr-1 md:grid-cols-2 lg:col-span-2 2xl:col-span-1 2xl:grid-cols-1 2xl:overflow-y-auto"
          >
            <Card
              size="sm"
              role="region"
              aria-labelledby="objects-workflow-title"
              className="min-w-0"
            >
              <CardHeader>
                <CardTitle id="objects-workflow-title">{t('objects.workflowLabel')}</CardTitle>
              </CardHeader>
              <CardContent>
                <ol className="text-sm" aria-label={t('objects.workflowLabel')}>
                  <WorkflowStep
                    index={1}
                    title={t('objects.workflowDraftTitle')}
                    detail={t('objects.workflowDraftDetail')}
                  />
                  <WorkflowStep
                    index={2}
                    title={t('objects.workflowFieldsTitle')}
                    detail={t('objects.workflowFieldsDetail')}
                  />
                  <WorkflowStep
                    index={3}
                    title={t('objects.workflowPublishTitle')}
                    detail={t('objects.workflowPublishDetail')}
                  />
                </ol>
              </CardContent>
            </Card>
          </aside>
        )}
      </div>
    </div>
  );
}

function FieldsExpansionButton({
  expanded,
  label,
  onToggle,
}: {
  expanded: boolean;
  label: string;
  onToggle: () => void;
}) {
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            aria-label={label}
            aria-pressed={expanded}
            onClick={onToggle}
          >
            {expanded ? (
              <Minimize2 className="size-4" aria-hidden />
            ) : (
              <Maximize2 className="size-4" aria-hidden />
            )}
          </Button>
        }
      />
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  );
}

function FeedbackAlert({ feedback }: { feedback: FeedbackState }) {
  const { t } = useTranslation();

  return (
    <Alert variant={feedback.variant === 'destructive' ? 'destructive' : 'default'}>
      {feedback.variant === 'success' ? (
        <CheckCircle2 className="size-4" aria-hidden />
      ) : (
        <AlertCircle className="size-4" aria-hidden />
      )}
      <AlertTitle>{t(feedback.titleKey)}</AlertTitle>
      {feedback.detail ? <AlertDescription>{feedback.detail}</AlertDescription> : null}
    </Alert>
  );
}

function currentValidationFeedback(
  feedback: FeedbackState,
  active: boolean,
  validation: FieldValidationResult,
): FeedbackState | null {
  if (!active) return feedback;
  if (validation.messages.length === 0) return null;

  return {
    ...feedback,
    detail: validation.messages.join(' '),
  };
}

function LockedControlIndicator() {
  return (
    <LockKeyhole
      className="pointer-events-none absolute right-2 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground transition-colors group-hover/locked-control:text-destructive group-focus-within/locked-control:text-destructive"
      aria-hidden
    />
  );
}

function DisabledActionHint({
  active,
  label,
  children,
}: {
  active: boolean;
  label: string;
  children: ReactNode;
}) {
  if (!active) return children;

  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <span className="block cursor-not-allowed rounded-lg" data-disabled-action-hint="true">
            {children}
          </span>
        }
      />
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  );
}

function WorkflowStep({ index, title, detail }: { index: number; title: string; detail: string }) {
  return (
    <li className="relative grid min-w-0 grid-cols-[1.75rem_minmax(0,1fr)] gap-3 pb-5 before:absolute before:left-3.5 before:top-7 before:bottom-0 before:w-px before:bg-border last:pb-0 last:before:hidden">
      <span className="relative z-10 flex size-7 shrink-0 items-center justify-center rounded-full border border-border bg-accent text-xs font-semibold text-accent-foreground shadow-sm dark:border-foreground/20 dark:bg-accent">
        {index}
      </span>
      <span className="min-w-0 pt-0.5">
        <span className="block font-medium text-foreground">{title}</span>
        <span className="mt-0.5 block text-muted-foreground">{detail}</span>
      </span>
    </li>
  );
}

function cacheObjectDefinitionResult(queryClient: QueryClient, definition: ObjectDefinitionDetail) {
  if (definition.id) {
    queryClient.setQueryData(objectDefinitionQueryKeys.detail(definition.id), definition);
  }

  void queryClient.invalidateQueries({ queryKey: objectDefinitionQueryKeys.lists() });
}

function deriveObjectKey(name: string) {
  if (!name.trim()) return '';

  const normalized = name
    .trim()
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/\u0111/g, 'd')
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '')
    .replace(/_{2,}/g, '_');

  const key = /^[a-z]/.test(normalized)
    ? normalized
    : normalized
      ? `object_${normalized}`
      : 'object';

  return key.slice(0, 63).replace(/_+$/g, '') || 'object';
}

function FieldEditor({
  field,
  index,
  disabled,
  onChange,
  onMove,
  onRemove,
  errors,
}: {
  field: EditableField;
  index: number;
  disabled: boolean;
  onChange: (patch: Partial<EditableField>) => void;
  onMove: (clientId: string, direction: -1 | 1) => void;
  onRemove: (clientId: string) => void;
  errors?: EditableFieldErrors;
}) {
  const { t } = useTranslation();
  const prefix = `field-${field.clientId}`;
  const hasErrors = Boolean(errors?.fieldKey || errors?.label);

  return (
    <div
      data-invalid={hasErrors}
      className="rounded-lg border border-border bg-background/45 p-3 data-[invalid=true]:border-destructive data-[invalid=true]:bg-destructive/5"
    >
      <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto]">
        <Field data-invalid={Boolean(errors?.fieldKey)}>
          <FieldLabel htmlFor={`${prefix}-key`}>{t('objects.fieldKey')}</FieldLabel>
          <Input
            id={`${prefix}-key`}
            value={field.fieldKey}
            aria-invalid={Boolean(errors?.fieldKey)}
            aria-describedby={errors?.fieldKey ? `${prefix}-key-error` : undefined}
            disabled={disabled}
            onChange={(event) => onChange({ fieldKey: event.target.value })}
          />
          <FieldError id={`${prefix}-key-error`}>{errors?.fieldKey}</FieldError>
        </Field>
        <Field data-invalid={Boolean(errors?.label)}>
          <FieldLabel htmlFor={`${prefix}-label`}>{t('objects.label')}</FieldLabel>
          <Input
            id={`${prefix}-label`}
            value={field.label}
            aria-invalid={Boolean(errors?.label)}
            aria-describedby={errors?.label ? `${prefix}-label-error` : undefined}
            disabled={disabled}
            onChange={(event) => onChange({ label: event.target.value })}
          />
          <FieldError id={`${prefix}-label-error`}>{errors?.label}</FieldError>
        </Field>
        <div className="flex items-end gap-1">
          <IconButton
            label={t('objects.moveUp')}
            disabled={disabled || index === 0}
            onClick={() => onMove(field.clientId, -1)}
          >
            <ArrowUp className="size-4" aria-hidden />
          </IconButton>
          <IconButton
            label={t('objects.moveDown')}
            disabled={disabled}
            onClick={() => onMove(field.clientId, 1)}
          >
            <ArrowDown className="size-4" aria-hidden />
          </IconButton>
          <IconButton
            label={t('objects.removeField')}
            disabled={disabled}
            onClick={() => onRemove(field.clientId)}
          >
            <Trash2 className="size-4" aria-hidden />
          </IconButton>
        </div>
      </div>
    </div>
  );
}

function IconButton({
  label,
  disabled,
  onClick,
  children,
}: {
  label: string;
  disabled: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      aria-label={label}
      title={label}
      disabled={disabled}
      onClick={onClick}
      className="size-8 px-0"
    >
      {children}
    </Button>
  );
}

function StatusBadge({ status }: { status: string }) {
  const { t } = useTranslation();
  const published = status === 'Published';

  return (
    <Badge variant={published ? 'secondary' : 'outline'}>
      {published ? t('objects.published') : t('objects.draft')}
    </Badge>
  );
}

function fromDefinitionFields(definition: ObjectDefinitionDetail): EditableField[] {
  return (definition.fields ?? []).map((field) => ({
    clientId: field.id ?? crypto.randomUUID(),
    fieldKey: field.fieldKey ?? '',
    label: field.label ?? '',
  }));
}

function toFieldInput(field: EditableField): ObjectFieldDefinitionInput {
  return {
    fieldKey: field.fieldKey.trim(),
    label: field.label.trim(),
  };
}

function validateFields(
  fields: readonly EditableField[],
  t: (key: string) => string,
  options: { requireFields: boolean },
): FieldValidationResult {
  const errors: string[] = [];
  const fieldErrors: Record<string, EditableFieldErrors> = {};
  const seenKeys = new Set<string>();

  if (options.requireFields && fields.length === 0) {
    errors.push(t('objects.validationFieldsRequired'));
  }

  for (const field of fields) {
    const trimmedKey = field.fieldKey.trim();

    if (!keyPattern.test(trimmedKey) || seenKeys.has(trimmedKey)) {
      addFieldValidationError(
        fieldErrors,
        errors,
        field.clientId,
        'fieldKey',
        t('objects.validationFieldKey'),
      );
    }
    seenKeys.add(trimmedKey);

    if (!field.label.trim()) {
      addFieldValidationError(
        fieldErrors,
        errors,
        field.clientId,
        'label',
        t('objects.validationFieldLabel'),
      );
    }
  }

  return {
    messages: [...new Set(errors)],
    fieldErrors,
  };
}

function emptyFieldValidationResult(): FieldValidationResult {
  return {
    messages: [],
    fieldErrors: {},
  };
}

function addFieldValidationError(
  fieldErrors: Record<string, EditableFieldErrors>,
  messages: string[],
  clientId: string,
  key: keyof EditableFieldErrors,
  message: string,
) {
  fieldErrors[clientId] = {
    ...fieldErrors[clientId],
    [key]: message,
  };
  messages.push(message);
}

function apiErrorFeedback(error: unknown, scope: FeedbackScope): FeedbackState {
  if (error instanceof ApiError) {
    const detail = apiErrorDetail(error.data);

    if (!detail) {
      return {
        scope,
        variant: 'destructive',
        titleKey: 'auth.genericError',
      };
    }

    return {
      scope,
      variant: 'destructive',
      titleKey:
        error.status === 400 || error.status === 409 || error.status === 422
          ? 'objects.validationTitle'
          : 'objects.requestError',
      detail,
    };
  }

  return {
    scope,
    variant: 'destructive',
    titleKey: 'auth.genericError',
  };
}

function applyIdentityApiErrors(
  error: unknown,
  setError: UseFormSetError<IdentityFormValues>,
): boolean {
  const messagesByField = apiErrorMessagesByField(error);
  const nameMessage = messagesByField.name?.[0];
  if (!nameMessage) return false;

  setError('name', {
    type: 'server',
    message: nameMessage,
  });
  return true;
}

function apiErrorDetail(data: unknown): string | undefined {
  const record = objectRecord(data);
  if (!record) return undefined;

  return (
    validationErrorsText(record.errors) ?? firstText(record.detail, record.title, record.message)
  );
}

function validationErrorsText(errors: unknown): string | undefined {
  const record = objectRecord(errors);
  if (!record) return undefined;

  const messages = Object.values(record)
    .flatMap((value) => (Array.isArray(value) ? value : [value]))
    .filter(isNonEmptyString);

  return messages.length > 0 ? [...new Set(messages)].join(' ') : undefined;
}

function apiErrorMessagesByField(error: unknown): Record<string, string[]> {
  if (!(error instanceof ApiError)) return {};

  const record = objectRecord(error.data);
  const errors = record ? objectRecord(record.errors) : undefined;
  if (!errors) return {};

  return Object.fromEntries(
    Object.entries(errors)
      .map(([key, value]) => [key.toLowerCase(), toMessageArray(value)] as const)
      .filter((entry): entry is readonly [string, string[]] => entry[1].length > 0),
  );
}

function toMessageArray(value: unknown): string[] {
  return (Array.isArray(value) ? value : [value]).filter(isNonEmptyString);
}

function firstText(...values: unknown[]): string | undefined {
  return values.find(isNonEmptyString);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function objectRecord(value: unknown): Record<string, unknown> | undefined {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return undefined;
  }

  return value as Record<string, unknown>;
}
