import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDown, ArrowUp, Plus, Save, Trash2, UploadCloud } from 'lucide-react';
import { type ReactNode, useEffect, useId, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { AsyncContent } from '@/components/shared/AsyncContent';
import {
  ManagedDialog,
  ManagedDialogAction,
  ManagedDialogAsyncAction,
  ManagedDialogBody,
} from '@/components/shared/ManagedDialog';
import { ManagedDialogTabs } from '@/components/shared/ManagedDialogTabs';
import { MetadataTag } from '@/components/shared/MetadataTag';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { StatusNotice } from '@/components/shared/StatusNotice';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { Button } from '@/components/ui/button';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Item,
  ItemActions,
  ItemContent,
  ItemGroup,
  ItemHeader,
  ItemTitle,
} from '@/components/ui/item';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { createRuleBinding, type RuleDefinitionSummary } from '@/features/rules';
import { ApiError } from '@/lib/api';
import { referenceContent } from '@/lib/reference-metadata';
import {
  type BusinessObjectChoiceSelectionMode,
  type BusinessObjectDefinitionDetail,
  type BusinessObjectFieldDefinitionInput,
  type BusinessObjectFieldType,
  businessObjectDefinitionDetailQueryOptions,
  businessObjectDefinitionQueryKeys,
  createBusinessObjectDefinition,
  publishBusinessObjectDefinition,
  saveUnpublishedBusinessObjectDefinition,
} from '../api';

const keyPattern = /^[a-z][a-z0-9_]{0,62}$/;
const fieldTypes = [
  'Text',
  'Integer',
  'Decimal',
  'Date',
  'DateTime',
  'Boolean',
  'Choice',
] as const satisfies readonly BusinessObjectFieldType[];

const optionSchema = z.object({
  clientId: z.string(),
  id: z.string().optional(),
  optionKey: z.string().trim().regex(keyPattern, 'businessObjects.validationOptionKey'),
  label: z.string().trim().min(1, 'businessObjects.validationOptionLabel'),
});

const appliedRuleSchema = z.object({
  clientId: z.string(),
  id: z.string().optional(),
  bindingId: z.string().uuid().optional(),
  definitionKey: z.string().optional(),
  definitionVersion: z.number().int().positive().optional(),
  inputs: z.record(z.string(), z.array(z.string())),
});

const fieldSchema = z
  .object({
    clientId: z.string(),
    id: z.string().optional(),
    fieldKey: z
      .string()
      .trim()
      .min(1, 'businessObjects.validationFieldKeyRequired')
      .regex(keyPattern, 'businessObjects.validationFieldKey'),
    label: z.string().trim().min(1, 'businessObjects.validationFieldLabel'),
    fieldType: z.enum(fieldTypes),
    choiceSelectionMode: z.enum(['Single', 'Multiple']),
    options: z.array(optionSchema),
    rules: z.array(appliedRuleSchema),
  })
  .superRefine((field, context) => {
    if (field.fieldType !== 'Choice') return;
    if (field.options.length === 0) {
      context.addIssue({
        code: 'custom',
        message: 'businessObjects.validationOptionsRequired',
        path: ['options'],
      });
    }
    const keys = new Set<string>();
    field.options.forEach((option, index) => {
      if (!keys.add(option.optionKey.trim())) {
        context.addIssue({
          code: 'custom',
          message: 'businessObjects.validationOptionsUnique',
          path: ['options', index, 'optionKey'],
        });
      }
    });
  });

const definitionSchema = z
  .object({
    name: z.string().trim().min(1, 'businessObjects.validationName'),
    fields: z.array(fieldSchema),
  })
  .superRefine((definition, context) => {
    const keys = new Set<string>();
    definition.fields.forEach((field, index) => {
      if (!keys.add(field.fieldKey.trim())) {
        context.addIssue({
          code: 'custom',
          message: 'businessObjects.validationFieldKey',
          path: ['fields', index, 'fieldKey'],
        });
      }
    });
  });

type DefinitionFormValues = z.infer<typeof definitionSchema>;
type EditableField = DefinitionFormValues['fields'][number];
type EditableOption = EditableField['options'][number];
type AppliedRule = EditableField['rules'][number];
type DialogMode = 'create' | 'edit' | 'view';
type RuleInputErrors = Record<string, string>;

interface BusinessObjectDefinitionDialogProps {
  mode?: DialogMode;
  recordId?: string;
  ruleDefinitions: RuleDefinitionSummary[];
  ruleCatalogLoading: boolean;
  ruleCatalogUnavailable: boolean;
  onCreated: (recordId: string, title: string) => void;
  onClose: () => void;
}

const emptyDefinition: DefinitionFormValues = { name: '', fields: [] };

export function BusinessObjectDefinitionDialog({
  mode,
  recordId,
  ruleDefinitions,
  ruleCatalogLoading,
  ruleCatalogUnavailable,
  onCreated,
  onClose,
}: BusinessObjectDefinitionDialogProps) {
  const { t, i18n } = useTranslation();
  const formId = useId();
  const queryClient = useQueryClient();
  const [requestError, setRequestError] = useState<string | null>(null);
  const [discardOpen, setDiscardOpen] = useState(false);
  const [publishOpen, setPublishOpen] = useState(false);
  const [activeSection, setActiveSection] = useState('general');
  const [ruleInputErrors, setRuleInputErrors] = useState<RuleInputErrors>({});
  const form = useForm<DefinitionFormValues>({
    resolver: zodResolver(definitionSchema),
    defaultValues: emptyDefinition,
  });
  const fields = form.watch('fields');
  const name = form.watch('name');
  const detailQuery = useQuery({
    ...businessObjectDefinitionDetailQueryOptions(recordId ?? ''),
    enabled: Boolean(recordId && mode !== 'create'),
  });
  const definition = detailQuery.data;
  const detailErrorStatus =
    detailQuery.error instanceof ApiError ? detailQuery.error.status : undefined;
  const detailTemporarilyUnavailable = detailErrorStatus === 503;
  const detailActionUnavailable = detailErrorStatus === 403 || detailErrorStatus === 404;
  const canSave = !detailQuery.isError && mode === 'edit' && definition?.actions?.canSave === true;
  const canPublish =
    !detailQuery.isError && mode === 'edit' && definition?.actions?.canPublish === true;
  const readOnly = mode === 'view' || (mode !== 'create' && !canSave);
  const open = Boolean(mode);
  const exitLabel =
    readOnly || (mode !== 'create' && !definition) ? t('app.close') : t('app.cancel');

  useEffect(() => {
    setRequestError(null);
    setRuleInputErrors({});
    if (mode === 'create') {
      form.reset(emptyDefinition);
      return;
    }
    if (definition) form.reset(toFormValues(definition, ruleDefinitions));
  }, [definition, form, mode, ruleDefinitions]);

  const createMutation = useMutation({
    mutationFn: createBusinessObjectDefinition,
    onSuccess: async (created) => {
      cacheDefinition(queryClient, created);
      form.reset(toFormValues(created, ruleDefinitions));
      await invalidateLists(queryClient);
      if (created.id) onCreated(created.id, created.name ?? t('businessObjects.definitionTitle'));
    },
    onError: (error) =>
      setRequestError(
        readApiError(
          error,
          t('businessObjects.requestError'),
          t('businessObjects.authorizationUnavailableDescription'),
          t('businessObjects.authorizationTemporarilyUnavailableDescription'),
        ),
      ),
  });

  const saveMutation = useMutation({
    mutationFn: async ({
      id,
      values,
      revision,
    }: {
      id: string;
      values: DefinitionFormValues;
      revision: number;
    }) =>
      saveUnpublishedBusinessObjectDefinition(id, {
        expectedRevision: revision,
        name: values.name.trim(),
        fields: toFieldInputs(
          await ensureRuleBindings(values.fields, ruleDefinitions, values.name),
          ruleDefinitions,
        ),
      }),
    onSuccess: async (saved) => {
      cacheDefinition(queryClient, saved);
      form.reset(toFormValues(saved, ruleDefinitions));
      setRequestError(null);
      await invalidateLists(queryClient);
    },
    onError: (error) =>
      setRequestError(
        readApiError(
          error,
          t('businessObjects.requestError'),
          t('businessObjects.authorizationUnavailableDescription'),
          t('businessObjects.authorizationTemporarilyUnavailableDescription'),
        ),
      ),
  });

  const publishMutation = useMutation({
    mutationFn: ({ id, revision }: { id: string; revision: number }) =>
      publishBusinessObjectDefinition(id, { expectedRevision: revision }),
    onSuccess: async (published) => {
      cacheDefinition(queryClient, published);
      form.reset(toFormValues(published, ruleDefinitions));
      setRequestError(null);
      setPublishOpen(false);
      await invalidateLists(queryClient);
    },
    onError: (error) =>
      setRequestError(
        readApiError(
          error,
          t('businessObjects.requestError'),
          t('businessObjects.authorizationUnavailableDescription'),
          t('businessObjects.authorizationTemporarilyUnavailableDescription'),
        ),
      ),
  });

  const busy = createMutation.isPending || saveMutation.isPending || publishMutation.isPending;
  const title =
    mode === 'create'
      ? t('businessObjects.defineTitle')
      : !detailQuery.isError && definition?.name
        ? definition.name
        : t('businessObjects.definitionTitle');

  function requestClose() {
    if (!readOnly && form.formState.isDirty) {
      setDiscardOpen(true);
      return;
    }
    onClose();
  }

  function updateFields(next: EditableField[]) {
    form.setValue('fields', next, { shouldDirty: true, shouldValidate: false });
  }

  function updateField(index: number, patch: Partial<EditableField>) {
    updateFields(
      fields.map((field, fieldIndex) => (fieldIndex === index ? { ...field, ...patch } : field)),
    );
  }

  function moveField(index: number, direction: -1 | 1) {
    const nextIndex = index + direction;
    if (nextIndex < 0 || nextIndex >= fields.length) return;
    const next = [...fields];
    [next[index], next[nextIndex]] = [next[nextIndex], next[index]];
    updateFields(next);
  }

  const submit = form.handleSubmit(
    (values) => {
      setRequestError(null);
      if (mode === 'create') {
        createMutation.mutate({ name: values.name.trim() });
        return;
      }
      if (canSave && definition?.id && definition.revision != null) {
        const ruleIssues = validateRuleBindings(values.fields, ruleDefinitions);
        setRuleInputErrors(toRuleInputErrors(ruleIssues, i18n.language, t));
        if (ruleIssues.length > 0) {
          setActiveSection('fields');
          setRequestError(formatRuleIssue(ruleIssues[0], i18n.language, t));
          return;
        }
        saveMutation.mutate({ id: definition.id, values, revision: definition.revision });
      }
    },
    (errors) => setActiveSection(errors.fields ? 'fields' : 'general'),
  );

  return (
    <>
      <ManagedDialog
        surfaceId="business-object-editor"
        open={open}
        onOpenChange={(nextOpen) => {
          if (!nextOpen) requestClose();
        }}
        title={title}
        description={
          mode === 'create'
            ? t('businessObjects.defineDescription')
            : t('businessObjects.editorDescription')
        }
        titleAccessory={
          definition && !detailQuery.isError ? (
            <StatusBadge state={definition.status === 'Published' ? 'positive' : 'neutral'}>
              {t(
                definition.status === 'Published'
                  ? 'businessObjects.published'
                  : 'businessObjects.unpublished',
              )}
            </StatusBadge>
          ) : null
        }
        closeDisabled={busy}
        dirty={!readOnly && form.formState.isDirty}
        footer={
          <>
            <ManagedDialogAction
              type="button"
              variant="outline"
              disabled={busy}
              onClick={requestClose}
            >
              {exitLabel}
            </ManagedDialogAction>
            {mode === 'create' ? (
              <ManagedDialogAsyncAction
                type="submit"
                form={formId}
                icon={<Plus />}
                pending={createMutation.isPending}
                pendingLabel={t('businessObjects.creating')}
              >
                {t('businessObjects.create')}
              </ManagedDialogAsyncAction>
            ) : null}
            {mode === 'edit' && (canSave || canPublish) ? (
              <>
                {canSave ? (
                  <ManagedDialogAsyncAction
                    type="submit"
                    form={formId}
                    variant="secondary"
                    disabled={busy || !form.formState.isDirty}
                    icon={<Save />}
                    pending={saveMutation.isPending}
                    pendingLabel={t('businessObjects.saving')}
                  >
                    {t('businessObjects.save')}
                  </ManagedDialogAsyncAction>
                ) : null}
                {canPublish ? (
                  <ManagedDialogAsyncAction
                    type="button"
                    disabled={busy || form.formState.isDirty || fields.length === 0}
                    icon={<UploadCloud />}
                    pending={publishMutation.isPending}
                    pendingLabel={t('businessObjects.publishing')}
                    onClick={() => setPublishOpen(true)}
                  >
                    {t('businessObjects.publish')}
                  </ManagedDialogAsyncAction>
                ) : null}
              </>
            ) : null}
          </>
        }
      >
        <form id={formId} className="contents" onSubmit={submit} noValidate>
          <ManagedDialogBody>
            <AsyncContent
              pending={detailQuery.isPending && mode !== 'create'}
              error={detailQuery.isError}
              pendingLabel={t('table.loading')}
            >
              {detailQuery.isError ? (
                <StatusNotice
                  tone={
                    detailActionUnavailable || detailTemporarilyUnavailable
                      ? 'warning'
                      : 'destructive'
                  }
                  title={
                    detailTemporarilyUnavailable
                      ? t('businessObjects.authorizationTemporarilyUnavailableTitle')
                      : detailActionUnavailable
                        ? t('businessObjects.authorizationUnavailableTitle')
                        : t('businessObjects.loadError')
                  }
                >
                  <span>
                    {detailTemporarilyUnavailable
                      ? t('businessObjects.authorizationTemporarilyUnavailableDescription')
                      : detailActionUnavailable
                        ? t('businessObjects.authorizationUnavailableDescription')
                        : t('businessObjects.loadErrorDescription')}
                  </span>
                  {detailTemporarilyUnavailable ? (
                    <>
                      {' '}
                      <Button
                        type="button"
                        variant="link"
                        disabled={detailQuery.isFetching}
                        onClick={() => void detailQuery.refetch()}
                      >
                        {t('app.retry')}
                      </Button>
                    </>
                  ) : null}
                </StatusNotice>
              ) : null}
              {requestError ? (
                <StatusNotice tone="destructive" title={t('businessObjects.requestError')}>
                  {requestError}
                </StatusNotice>
              ) : null}

              {!detailQuery.isError && readOnly && definition ? (
                <BusinessObjectReadOnlyDetails definition={definition} />
              ) : (!detailQuery.isPending && !detailQuery.isError) || mode === 'create' ? (
                <ManagedDialogTabs
                  label={t('businessObjects.definitionSections')}
                  generalLabel={t('dialog.general')}
                  activeSection={activeSection}
                  onActiveSectionChange={setActiveSection}
                  general={
                    <DefinitionDetails
                      name={name}
                      objectKey={definition?.objectKey ?? deriveKey(name)}
                      readOnly={readOnly}
                      nameError={form.formState.errors.name?.message}
                      onNameChange={(value) =>
                        form.setValue('name', value, { shouldDirty: true, shouldValidate: true })
                      }
                    />
                  }
                  sections={[
                    ...(mode !== 'create'
                      ? [
                          {
                            id: 'fields',
                            label: t('businessObjects.fields'),
                            content: (
                              <FieldsEditor
                                fields={fields}
                                errors={form.formState.errors.fields}
                                readOnly={readOnly}
                                ruleDefinitions={ruleDefinitions}
                                ruleCatalogLoading={ruleCatalogLoading}
                                ruleCatalogUnavailable={ruleCatalogUnavailable}
                                ruleInputErrors={ruleInputErrors}
                                onChange={updateField}
                                onRuleInputChange={(ruleId, inputKey) =>
                                  setRuleInputErrors((current) => {
                                    const key = ruleInputErrorKey(ruleId, inputKey);
                                    if (!current[key]) return current;
                                    const { [key]: _, ...next } = current;
                                    return next;
                                  })
                                }
                                onMove={moveField}
                                onRemove={(index) =>
                                  updateFields(
                                    fields.filter((_, fieldIndex) => fieldIndex !== index),
                                  )
                                }
                                onAdd={() => updateFields([...fields, newField()])}
                              />
                            ),
                          },
                        ]
                      : []),
                    ...(definition?.latestPublishedVersion
                      ? [
                          {
                            id: 'published',
                            label: t('businessObjects.publishedVersion'),
                            content: <PublishedVersion definition={definition} />,
                          },
                        ]
                      : []),
                  ]}
                />
              ) : null}
            </AsyncContent>
          </ManagedDialogBody>
        </form>
      </ManagedDialog>

      <AlertDialog open={discardOpen} onOpenChange={setDiscardOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('businessObjects.discardTitle')}</AlertDialogTitle>
            <AlertDialogDescription>
              {t('businessObjects.discardDescription')}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('businessObjects.keepEditing')}</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              onClick={() => {
                setDiscardOpen(false);
                form.reset();
                onClose();
              }}
            >
              {t('businessObjects.discard')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog open={publishOpen} onOpenChange={setPublishOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('businessObjects.publishTitle')}</AlertDialogTitle>
            <AlertDialogDescription>
              {t('businessObjects.publishDescription')}
            </AlertDialogDescription>
          </AlertDialogHeader>
          {definition ? (
            <BusinessObjectPublishReview definition={definition} fields={fields} />
          ) : null}
          {requestError ? <StatusNotice tone="destructive">{requestError}</StatusNotice> : null}
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>{t('app.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              disabled={busy || !definition?.id || definition.revision == null}
              onClick={() => {
                if (definition?.id && definition.revision != null) {
                  publishMutation.mutate({
                    id: definition.id,
                    revision: definition.revision,
                  });
                }
              }}
            >
              {t('businessObjects.publish')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}

function BusinessObjectReadOnlyDetails({
  definition,
}: {
  definition: BusinessObjectDefinitionDetail;
}) {
  const { t, i18n } = useTranslation();
  const detailsId = useId();
  const published = definition.status === 'Published' && definition.latestPublishedVersion;
  const fields = published
    ? (definition.latestPublishedVersion?.fields ?? [])
    : (definition.fields ?? []);
  const publishedAt = definition.latestPublishedVersion?.publishedAt;
  const dateFormatter = new Intl.DateTimeFormat(i18n.language, {
    dateStyle: 'medium',
    timeStyle: 'short',
  });

  return (
    <div
      data-slot="business-object-read-only-details"
      className="@container/business-object-details"
    >
      <ManagedDialogTabs
        label={t('businessObjects.definitionSections')}
        generalLabel={t('dialog.general')}
        general={
          <div className="space-y-5">
            <p className="text-sm leading-relaxed text-muted-foreground">
              {t(
                published
                  ? 'businessObjects.readOnlyPublishedSummary'
                  : 'businessObjects.readOnlyUnpublishedSummary',
              )}
            </p>
            <dl className="grid gap-5 @md/business-object-details:grid-cols-2">
              <ReadOnlyFact
                label={t('businessObjects.name')}
                value={definition.name ?? t('businessObjects.notAvailable')}
              />
              <ReadOnlyFact
                label={t('businessObjects.objectKey')}
                value={definition.objectKey ?? t('businessObjects.notAvailable')}
              />
              <ReadOnlyFact
                label={t('businessObjects.version')}
                value={
                  definition.latestPublishedVersionNumber
                    ? t('businessObjects.latestVersion', {
                        version: definition.latestPublishedVersionNumber,
                      })
                    : t('businessObjects.notAvailable')
                }
              />
              <ReadOnlyFact
                label={t('businessObjects.fields')}
                value={t('businessObjects.fieldCount', { count: fields.length })}
              />
              <ReadOnlyFact
                label={
                  publishedAt ? t('businessObjects.publishedAt') : t('businessObjects.updated')
                }
                value={formatBusinessObjectDate(
                  publishedAt ?? definition.updatedAt,
                  dateFormatter,
                  t('businessObjects.notAvailable'),
                )}
              />
            </dl>
          </div>
        }
        sections={[
          {
            id: 'fields',
            label: t('businessObjects.fields'),
            content: (
              <section aria-labelledby={`${detailsId}-fields`} className="space-y-4">
                <ReadOnlyHeading
                  id={`${detailsId}-fields`}
                  title={t('businessObjects.fields')}
                  description={t('businessObjects.fieldsDescription')}
                />
                {fields.length === 0 ? (
                  <p className="text-sm text-muted-foreground">
                    {t('businessObjects.noFieldsDescription')}
                  </p>
                ) : (
                  <ol className="divide-y divide-border">
                    {fields.map((field, index) => (
                      <li
                        key={field.id ?? field.fieldKey ?? index}
                        className="space-y-4 py-5 first:pt-0 last:pb-0"
                      >
                        <div>
                          <h4 className="text-sm font-semibold text-foreground">
                            {field.label ?? t('businessObjects.newField')}
                          </h4>
                          <div className="mt-2 flex flex-wrap gap-2">
                            <MetadataTag>{field.fieldKey ?? '—'}</MetadataTag>
                            <MetadataTag>
                              {t(`businessObjects.fieldType${field.fieldType ?? 'Text'}`)}
                            </MetadataTag>
                          </div>
                        </div>

                        {field.choiceConfiguration ? (
                          <dl className="grid gap-4 @md/business-object-details:grid-cols-2">
                            <ReadOnlyFact
                              label={t('businessObjects.selectionMode')}
                              value={t(
                                field.choiceConfiguration.selectionMode === 'Multiple'
                                  ? 'businessObjects.selectionMultiple'
                                  : 'businessObjects.selectionSingle',
                              )}
                            />
                            <ReadOnlyFact
                              label={t('businessObjects.options')}
                              value={
                                (field.choiceConfiguration.options ?? []).length > 0 ? (
                                  <ul className="space-y-1 font-normal">
                                    {(field.choiceConfiguration.options ?? []).map((option) => (
                                      <li key={option.id ?? option.optionKey}>
                                        {option.label ?? option.optionKey ?? '—'}
                                        {option.optionKey ? (
                                          <span className="text-muted-foreground">
                                            {' '}
                                            ({option.optionKey})
                                          </span>
                                        ) : null}
                                      </li>
                                    ))}
                                  </ul>
                                ) : (
                                  t('businessObjects.notAvailable')
                                )
                              }
                            />
                          </dl>
                        ) : null}

                        {(field.rules ?? []).length > 0 ? (
                          <ReadOnlyFact
                            label={t('businessObjects.fieldRulesTitle')}
                            value={
                              <ul className="space-y-2 font-normal">
                                {(field.rules ?? []).map((rule, ruleIndex) => (
                                  <li key={rule.id ?? rule.bindingId ?? ruleIndex}>
                                    <span className="font-medium">
                                      {t('businessObjects.bindingId')}:{' '}
                                    </span>
                                    <span>
                                      {rule.bindingId ?? t('businessObjects.notAvailable')}
                                    </span>
                                    <span className="text-muted-foreground">
                                      {` · ${t('businessObjects.bindingRevision')}: ${
                                        rule.bindingRevision ?? t('businessObjects.notAvailable')
                                      }`}
                                    </span>
                                  </li>
                                ))}
                              </ul>
                            }
                          />
                        ) : null}
                      </li>
                    ))}
                  </ol>
                )}
              </section>
            ),
          },
        ]}
      />
    </div>
  );
}

function BusinessObjectPublishReview({
  definition,
  fields,
}: {
  definition: BusinessObjectDefinitionDetail;
  fields: EditableField[];
}) {
  const { t } = useTranslation();
  const configuredRules = fields.reduce((count, field) => count + field.rules.length, 0);
  return (
    <dl className="grid grid-cols-2 gap-x-4 gap-y-3">
      <ReadOnlyFact
        label={t('businessObjects.name')}
        value={definition.name ?? t('businessObjects.notAvailable')}
      />
      <ReadOnlyFact
        label={t('businessObjects.objectKey')}
        value={definition.objectKey ?? t('businessObjects.notAvailable')}
      />
      <ReadOnlyFact
        label={t('businessObjects.fields')}
        value={t('businessObjects.fieldCount', { count: fields.length })}
      />
      <ReadOnlyFact
        label={t('businessObjects.fieldRulesTitle')}
        value={t('businessObjects.configuredRulesCount', { count: configuredRules })}
      />
    </dl>
  );
}

function ReadOnlyHeading({
  id,
  title,
  description,
}: {
  id: string;
  title: string;
  description: string;
}) {
  return (
    <div>
      <h3 id={id} className="text-sm font-semibold text-foreground">
        {title}
      </h3>
      <p className="mt-1 text-xs/relaxed text-muted-foreground">{description}</p>
    </div>
  );
}

function ReadOnlyFact({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="space-y-2">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="text-sm leading-relaxed font-medium text-foreground">{value}</dd>
    </div>
  );
}

function formatBusinessObjectDate(
  value: string | null | undefined,
  formatter: Intl.DateTimeFormat,
  fallback: string,
) {
  return value ? formatter.format(new Date(value)) : fallback;
}

function DefinitionDetails({
  name,
  objectKey,
  readOnly,
  nameError,
  onNameChange,
}: {
  name: string;
  objectKey: string;
  readOnly: boolean;
  nameError?: string;
  onNameChange: (value: string) => void;
}) {
  const { t } = useTranslation();
  const inputId = useId();
  const nameInputId = `${inputId}-name`;
  const objectKeyInputId = `${inputId}-object-key`;
  return (
    <div className="grid gap-4 md:grid-cols-2">
      <Field data-invalid={Boolean(nameError)}>
        <FieldLabel htmlFor={nameInputId}>{t('businessObjects.name')}</FieldLabel>
        <Input
          id={nameInputId}
          value={name}
          readOnly={readOnly}
          aria-invalid={Boolean(nameError)}
          onChange={(event) => onNameChange(event.target.value)}
        />
        <FieldError>{nameError ? t(nameError) : null}</FieldError>
        <FieldDescription>{t('businessObjects.nameHelp')}</FieldDescription>
      </Field>
      <Field>
        <FieldLabel htmlFor={objectKeyInputId}>{t('businessObjects.objectKey')}</FieldLabel>
        <Input id={objectKeyInputId} value={objectKey} readOnly aria-readonly="true" />
        <FieldDescription>{t('businessObjects.objectKeyHelp')}</FieldDescription>
      </Field>
    </div>
  );
}

function FieldsEditor({
  fields,
  errors,
  readOnly,
  ruleDefinitions,
  ruleCatalogLoading,
  ruleCatalogUnavailable,
  ruleInputErrors,
  onChange,
  onRuleInputChange,
  onMove,
  onRemove,
  onAdd,
}: {
  fields: EditableField[];
  errors: ReturnType<typeof useForm<DefinitionFormValues>>['formState']['errors']['fields'];
  readOnly: boolean;
  ruleDefinitions: RuleDefinitionSummary[];
  ruleCatalogLoading: boolean;
  ruleCatalogUnavailable: boolean;
  ruleInputErrors: RuleInputErrors;
  onChange: (index: number, patch: Partial<EditableField>) => void;
  onRuleInputChange: (ruleId: string, inputKey: string) => void;
  onMove: (index: number, direction: -1 | 1) => void;
  onRemove: (index: number) => void;
  onAdd: () => void;
}) {
  const { t } = useTranslation();
  return (
    <div>
      {!readOnly ? (
        <div className="mb-4">
          <Button type="button" variant="outline" onClick={onAdd}>
            <Plus aria-hidden />
            {t('businessObjects.addField')}
          </Button>
        </div>
      ) : null}
      {fields.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('businessObjects.noFieldsDescription')}</p>
      ) : null}
      <ItemGroup>
        {fields.map((field, index) => (
          <Item key={field.clientId} variant="outline">
            <ItemHeader>
              <ItemTitle>{field.label || t('businessObjects.newField')}</ItemTitle>
              {!readOnly ? (
                <ItemActions>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    aria-label={t('businessObjects.moveUp')}
                    disabled={index === 0}
                    onClick={() => onMove(index, -1)}
                  >
                    <ArrowUp aria-hidden />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    aria-label={t('businessObjects.moveDown')}
                    disabled={index === fields.length - 1}
                    onClick={() => onMove(index, 1)}
                  >
                    <ArrowDown aria-hidden />
                  </Button>
                  <Button
                    type="button"
                    variant="destructive"
                    size="icon-sm"
                    aria-label={t('businessObjects.removeField')}
                    onClick={() => onRemove(index)}
                  >
                    <Trash2 aria-hidden />
                  </Button>
                </ItemActions>
              ) : null}
            </ItemHeader>
            <ItemContent>
              <div className="grid gap-4 md:grid-cols-3">
                <Field data-invalid={Boolean(errors?.[index]?.label)}>
                  <FieldLabel htmlFor={`field-${field.clientId}-label`}>
                    {t('businessObjects.label')}
                  </FieldLabel>
                  <Input
                    id={`field-${field.clientId}-label`}
                    value={field.label}
                    readOnly={readOnly}
                    aria-invalid={Boolean(errors?.[index]?.label)}
                    onChange={(event) => onChange(index, { label: event.target.value })}
                  />
                  <FieldError>
                    {errors?.[index]?.label?.message ? t(errors[index].label.message) : null}
                  </FieldError>
                </Field>
                <Field data-invalid={Boolean(errors?.[index]?.fieldKey)}>
                  <FieldLabel htmlFor={`field-${field.clientId}-key`}>
                    {t('businessObjects.fieldKey')}
                  </FieldLabel>
                  <Input
                    id={`field-${field.clientId}-key`}
                    value={field.fieldKey}
                    readOnly={readOnly || Boolean(field.id)}
                    aria-invalid={Boolean(errors?.[index]?.fieldKey)}
                    onChange={(event) => onChange(index, { fieldKey: event.target.value })}
                  />
                  <FieldError>
                    {errors?.[index]?.fieldKey?.message ? t(errors[index].fieldKey.message) : null}
                  </FieldError>
                </Field>
                <Field>
                  <FieldLabel htmlFor={`field-${field.clientId}-type`}>
                    {t('businessObjects.fieldType')}
                  </FieldLabel>
                  <Select
                    value={field.fieldType}
                    disabled={readOnly}
                    onValueChange={(value) =>
                      onChange(index, {
                        fieldType: value as BusinessObjectFieldType,
                        rules: [],
                        options: value === 'Choice' ? field.options : [],
                      })
                    }
                  >
                    <SelectTrigger id={`field-${field.clientId}-type`}>
                      <SelectValue>{t(`businessObjects.fieldType${field.fieldType}`)}</SelectValue>
                    </SelectTrigger>
                    <SelectContent>
                      {fieldTypes.map((type) => (
                        <SelectItem key={type} value={type}>
                          {t(`businessObjects.fieldType${type}`)}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </Field>
              </div>

              {field.fieldType === 'Choice' ? (
                <ChoiceOptionsEditor
                  field={field}
                  index={index}
                  errors={errors?.[index]?.options}
                  readOnly={readOnly}
                  onChange={onChange}
                />
              ) : null}

              <RulesEditor
                field={field}
                index={index}
                definitions={ruleDefinitions}
                loading={ruleCatalogLoading}
                unavailable={ruleCatalogUnavailable}
                readOnly={readOnly}
                inputErrors={ruleInputErrors}
                onChange={onChange}
                onInputChange={onRuleInputChange}
              />
            </ItemContent>
          </Item>
        ))}
      </ItemGroup>
    </div>
  );
}

function ChoiceOptionsEditor({
  field,
  index,
  errors,
  readOnly,
  onChange,
}: {
  field: EditableField;
  index: number;
  errors: unknown;
  readOnly: boolean;
  onChange: (index: number, patch: Partial<EditableField>) => void;
}) {
  const { t } = useTranslation();
  const optionErrors = Array.isArray(errors) ? errors : [];

  function updateOption(optionIndex: number, patch: Partial<EditableOption>) {
    onChange(index, {
      options: field.options.map((option, currentIndex) =>
        currentIndex === optionIndex ? { ...option, ...patch } : option,
      ),
    });
  }

  return (
    <section aria-label={t('businessObjects.options')} className="mt-4">
      <div className="grid gap-4 md:grid-cols-2">
        <Field>
          <FieldLabel htmlFor={`field-${field.clientId}-selection-mode`}>
            {t('businessObjects.selectionMode')}
          </FieldLabel>
          <Select
            value={field.choiceSelectionMode}
            disabled={readOnly}
            onValueChange={(value) =>
              onChange(index, { choiceSelectionMode: value as BusinessObjectChoiceSelectionMode })
            }
          >
            <SelectTrigger id={`field-${field.clientId}-selection-mode`}>
              <SelectValue>
                {field.choiceSelectionMode === 'Single'
                  ? t('businessObjects.selectionSingle')
                  : t('businessObjects.selectionMultiple')}
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="Single">{t('businessObjects.selectionSingle')}</SelectItem>
              <SelectItem value="Multiple">{t('businessObjects.selectionMultiple')}</SelectItem>
            </SelectContent>
          </Select>
        </Field>
      </div>
      <div className="mt-4 flex items-center justify-between gap-3">
        <h4 className="text-sm font-medium">{t('businessObjects.options')}</h4>
        {!readOnly ? (
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => onChange(index, { options: [...field.options, newOption()] })}
          >
            <Plus aria-hidden />
            {t('businessObjects.addOption')}
          </Button>
        ) : null}
      </div>
      <ItemGroup className="mt-3">
        {field.options.map((option, optionIndex) => {
          const currentErrors = optionErrors[optionIndex] as
            | { optionKey?: { message?: string }; label?: { message?: string } }
            | undefined;
          return (
            <Item key={option.clientId} variant="muted" size="sm">
              <ItemContent>
                <div className="grid gap-3 md:grid-cols-2">
                  <Field data-invalid={Boolean(currentErrors?.optionKey)}>
                    <FieldLabel htmlFor={`option-${option.clientId}-key`}>
                      {t('businessObjects.optionKey')}
                    </FieldLabel>
                    <Input
                      id={`option-${option.clientId}-key`}
                      value={option.optionKey}
                      readOnly={readOnly || Boolean(option.id)}
                      aria-invalid={Boolean(currentErrors?.optionKey)}
                      onChange={(event) =>
                        updateOption(optionIndex, { optionKey: event.target.value })
                      }
                    />
                    <FieldError>
                      {currentErrors?.optionKey?.message
                        ? t(currentErrors.optionKey.message)
                        : null}
                    </FieldError>
                  </Field>
                  <Field data-invalid={Boolean(currentErrors?.label)}>
                    <FieldLabel htmlFor={`option-${option.clientId}-label`}>
                      {t('businessObjects.label')}
                    </FieldLabel>
                    <Input
                      id={`option-${option.clientId}-label`}
                      value={option.label}
                      readOnly={readOnly}
                      aria-invalid={Boolean(currentErrors?.label)}
                      onChange={(event) => updateOption(optionIndex, { label: event.target.value })}
                    />
                    <FieldError>
                      {currentErrors?.label?.message ? t(currentErrors.label.message) : null}
                    </FieldError>
                  </Field>
                </div>
              </ItemContent>
              {!readOnly ? (
                <ItemActions>
                  <Button
                    type="button"
                    variant="destructive"
                    size="icon-sm"
                    aria-label={t('businessObjects.removeOption')}
                    onClick={() =>
                      onChange(index, {
                        options: field.options.filter(
                          (_, currentIndex) => currentIndex !== optionIndex,
                        ),
                      })
                    }
                  >
                    <Trash2 aria-hidden />
                  </Button>
                </ItemActions>
              ) : null}
            </Item>
          );
        })}
      </ItemGroup>
    </section>
  );
}

function RulesEditor({
  field,
  index,
  definitions,
  loading,
  unavailable,
  readOnly,
  inputErrors,
  onChange,
  onInputChange,
}: {
  field: EditableField;
  index: number;
  definitions: RuleDefinitionSummary[];
  loading: boolean;
  unavailable: boolean;
  readOnly: boolean;
  inputErrors: RuleInputErrors;
  onChange: (index: number, patch: Partial<EditableField>) => void;
  onInputChange: (ruleId: string, inputKey: string) => void;
}) {
  const { t, i18n } = useTranslation();
  const compatible = definitions.filter(
    (definition) =>
      isCompatibleRule(definition, field) &&
      !field.rules.some((rule) => rule.definitionKey === definition.definitionKey),
  );

  function updateRule(ruleIndex: number, patch: Partial<AppliedRule>) {
    onChange(index, {
      rules: field.rules.map((rule, currentIndex) =>
        currentIndex === ruleIndex ? { ...rule, ...patch } : rule,
      ),
    });
  }

  return (
    <section aria-label={t('businessObjects.fieldRulesTitle')} className="mt-5">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h4 className="text-sm font-medium">{t('businessObjects.fieldRulesTitle')}</h4>
          <p className="text-sm text-muted-foreground">
            {t('businessObjects.fieldRulesDescription', {
              fieldType: t(`businessObjects.fieldType${field.fieldType}`),
            })}
          </p>
        </div>
        {!readOnly && !loading && !unavailable && compatible.length > 0 ? (
          <Select
            value=""
            onValueChange={(definitionKey) => {
              const definition = compatible.find(
                (candidate) => candidate.definitionKey === definitionKey,
              );
              if (!definition?.definitionKey || !definition.activeVersion) return;
              onChange(index, {
                rules: [...field.rules, newAppliedRule(definition)],
              });
            }}
          >
            <SelectTrigger aria-label={t('businessObjects.addRule')}>
              <SelectValue>{t('businessObjects.addRule')}</SelectValue>
            </SelectTrigger>
            <SelectContent>
              {compatible.map((definition) => (
                <SelectItem key={definition.definitionKey} value={definition.definitionKey ?? ''}>
                  {ruleDisplayName(definition, i18n.language, t)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        ) : null}
      </div>
      <AsyncContent
        className="mt-3"
        pending={loading}
        error={unavailable}
        pendingLabel={t('businessObjects.rulesCatalogLoading')}
      >
        {unavailable ? (
          <StatusNotice
            tone="destructive"
            title={t('businessObjects.rulesCatalogUnavailableTitle')}
          >
            {t('businessObjects.rulesCatalogUnavailableDescription')}
          </StatusNotice>
        ) : (
          <ItemGroup>
            {field.rules.map((rule, ruleIndex) => {
              const definition = definitions.find(
                (candidate) => candidate.definitionKey === rule.definitionKey,
              );
              return (
                <Item key={rule.clientId} variant="muted" size="sm">
                  <ItemHeader>
                    <ItemTitle>
                      {definition
                        ? ruleDisplayName(definition, i18n.language, t)
                        : (rule.bindingId ??
                          rule.definitionKey ??
                          t('businessObjects.unknownRule'))}
                    </ItemTitle>
                    <ItemActions>
                      {rule.definitionVersion ? (
                        <span className="text-xs text-muted-foreground">
                          {t('businessObjects.ruleVersion', { version: rule.definitionVersion })}
                        </span>
                      ) : null}
                      {!readOnly ? (
                        <Button
                          type="button"
                          variant="destructive"
                          size="icon-sm"
                          aria-label={t('businessObjects.removeRule')}
                          onClick={() =>
                            onChange(index, {
                              rules: field.rules.filter(
                                (_, currentIndex) => currentIndex !== ruleIndex,
                              ),
                            })
                          }
                        >
                          <Trash2 aria-hidden />
                        </Button>
                      ) : null}
                    </ItemActions>
                  </ItemHeader>
                  <ItemContent>
                    {(definition?.inputs ?? [])
                      .filter((input) => input.key !== 'value')
                      .map((input) => (
                        <RuleInputEditor
                          key={input.key}
                          input={input}
                          idPrefix={`${field.clientId}-${rule.clientId}`}
                          contextLabel={`${field.label || field.fieldKey}: ${
                            definition
                              ? ruleDisplayName(definition, i18n.language, t)
                              : (rule.definitionKey ?? t('businessObjects.unknownRule'))
                          }`}
                          values={rule.inputs[input.key ?? ''] ?? []}
                          readOnly={readOnly}
                          error={inputErrors[ruleInputErrorKey(rule.clientId, input.key ?? '')]}
                          onChange={(values) => {
                            onInputChange(rule.clientId, input.key ?? '');
                            updateRule(ruleIndex, {
                              inputs: { ...rule.inputs, [input.key ?? '']: values },
                            });
                          }}
                        />
                      ))}
                  </ItemContent>
                </Item>
              );
            })}
          </ItemGroup>
        )}
      </AsyncContent>
      {!loading && !unavailable && compatible.length === 0 && field.rules.length === 0 ? (
        <p className="mt-3 text-sm text-muted-foreground">
          {t('businessObjects.noCompatibleRules')}
        </p>
      ) : null}
    </section>
  );
}

type RuleInput = NonNullable<RuleDefinitionSummary['inputs']>[number];
type RuleInputValueType = NonNullable<RuleInput['types']>[number];

function RuleInputEditor({
  input,
  idPrefix,
  contextLabel,
  values,
  readOnly,
  error,
  onChange,
}: {
  input: RuleInput;
  idPrefix: string;
  contextLabel: string;
  values: string[];
  readOnly: boolean;
  error?: string;
  onChange: (values: string[]) => void;
}) {
  const { t } = useTranslation();
  const key = input.key ?? '';
  const label = ruleInputLabel(input, t);
  const accessibleLabel = `${label} (${contextLabel})`;
  const inputId = `rule-input-${idPrefix}-${key}`;
  const currentValues = input.allowMultiple
    ? values.length > 0
      ? values
      : ['']
    : [values[0] ?? ''];
  const [valueIds, setValueIds] = useState(() => currentValues.map(() => crypto.randomUUID()));

  function removeValue(valueIndex: number) {
    setValueIds((current) => current.filter((_, index) => index !== valueIndex));
    onChange(currentValues.filter((_, index) => index !== valueIndex));
  }

  function addValue() {
    setValueIds((current) => [...current, crypto.randomUUID()]);
    onChange([...currentValues, '']);
  }

  return (
    <Field data-invalid={Boolean(error)}>
      <FieldLabel htmlFor={`${inputId}-0`}>
        {label}
        <span className="sr-only">{` (${contextLabel})`}</span>
      </FieldLabel>
      <div className="flex flex-col gap-2">
        {currentValues.map((value, valueIndex) => (
          <div key={valueIds[valueIndex]} className="flex items-center gap-2">
            {valueIndex > 0 ? (
              <FieldLabel className="sr-only" htmlFor={`${inputId}-${valueIndex}`}>
                {`${accessibleLabel} ${valueIndex + 1}`}
              </FieldLabel>
            ) : null}
            <RuleInputValue
              id={`${inputId}-${valueIndex}`}
              input={input}
              value={value}
              readOnly={readOnly}
              invalid={Boolean(error)}
              onChange={(nextValue) =>
                onChange(
                  currentValues.map((current, index) =>
                    index === valueIndex ? nextValue : current,
                  ),
                )
              }
            />
            {!readOnly && input.allowMultiple && currentValues.length > 1 ? (
              <Button
                type="button"
                variant="destructive"
                size="icon-sm"
                aria-label={t('businessObjects.removeParameterValue')}
                onClick={() => removeValue(valueIndex)}
              >
                <Trash2 aria-hidden />
              </Button>
            ) : null}
          </div>
        ))}
      </div>
      {!readOnly && input.allowMultiple ? (
        <Button type="button" variant="outline" size="sm" onClick={addValue}>
          <Plus aria-hidden />
          {t('businessObjects.addParameterValue')}
        </Button>
      ) : null}
      <FieldError>{error}</FieldError>
    </Field>
  );
}

function RuleInputValue({
  id,
  input,
  value,
  readOnly,
  invalid,
  onChange,
}: {
  id: string;
  input: RuleInput;
  value: string;
  readOnly: boolean;
  invalid: boolean;
  onChange: (value: string) => void;
}) {
  const { t } = useTranslation();
  const options =
    input.allowedValues?.map((allowed) => ({ value: allowed, label: allowed })) ??
    (input.types?.[0] === 'Boolean'
      ? [
          { value: 'true', label: t('table.trueValue') },
          { value: 'false', label: t('table.falseValue') },
        ]
      : []);
  if (options.length > 0) {
    return (
      <Select
        value={value}
        disabled={readOnly}
        onValueChange={(nextValue) => {
          if (nextValue !== null) onChange(nextValue);
        }}
      >
        <SelectTrigger id={id} aria-invalid={invalid}>
          <SelectValue>
            {options.find((option) => option.value === value)?.label ?? value}
          </SelectValue>
        </SelectTrigger>
        <SelectContent>
          {options.map((option) => (
            <SelectItem key={option.value} value={option.value}>
              {option.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    );
  }
  return (
    <Input
      id={id}
      type={parameterInputType(input.types?.[0])}
      value={value}
      readOnly={readOnly}
      aria-invalid={invalid}
      onChange={(event) => onChange(event.target.value)}
    />
  );
}

function PublishedVersion({ definition }: { definition: BusinessObjectDefinitionDetail }) {
  const { t } = useTranslation();
  const version = definition.latestPublishedVersion;
  if (!version) return null;
  return (
    <div>
      <p className="text-sm text-muted-foreground">
        {t('businessObjects.publishedVersionSummary', {
          version: version.versionNumber,
          count: version.fields?.length ?? 0,
        })}
      </p>
      <ItemGroup className="mt-4">
        {(version.fields ?? []).map((field) => (
          <Item key={field.id ?? field.fieldKey} variant="outline" size="sm">
            <ItemContent>
              <ItemTitle>{field.label}</ItemTitle>
              <div className="flex flex-wrap gap-2">
                <MetadataTag>{field.fieldKey}</MetadataTag>
                <MetadataTag>
                  {t(`businessObjects.fieldType${field.fieldType ?? 'Text'}`)}
                </MetadataTag>
              </div>
            </ItemContent>
          </Item>
        ))}
      </ItemGroup>
    </div>
  );
}

function newField(): EditableField {
  return {
    clientId: crypto.randomUUID(),
    fieldKey: '',
    label: '',
    fieldType: 'Text',
    choiceSelectionMode: 'Single',
    options: [],
    rules: [],
  };
}

function newOption(): EditableOption {
  return { clientId: crypto.randomUUID(), optionKey: '', label: '' };
}

function newAppliedRule(definition: RuleDefinitionSummary): AppliedRule {
  return {
    clientId: crypto.randomUUID(),
    bindingId: undefined,
    definitionKey: definition.definitionKey ?? '',
    definitionVersion: definition.activeVersion ?? 1,
    inputs: Object.fromEntries(
      (definition.inputs ?? [])
        .filter((input) => input.key !== 'value')
        .map((input) => [input.key ?? '', input.isRequired ? [''] : []]),
    ),
  };
}

function toFormValues(
  definition: BusinessObjectDefinitionDetail,
  _ruleDefinitions: RuleDefinitionSummary[],
): DefinitionFormValues {
  return {
    name: definition.name ?? '',
    fields: (definition.fields ?? []).map((field) => ({
      clientId: field.id ?? crypto.randomUUID(),
      id: field.id,
      fieldKey: field.fieldKey ?? '',
      label: field.label ?? '',
      fieldType: field.fieldType ?? 'Text',
      choiceSelectionMode: field.choiceConfiguration?.selectionMode ?? 'Single',
      options: (field.choiceConfiguration?.options ?? []).map((option) => ({
        clientId: option.id ?? crypto.randomUUID(),
        id: option.id,
        optionKey: option.optionKey ?? '',
        label: option.label ?? '',
      })),
      rules: (field.rules ?? []).map((rule) => {
        return {
          clientId: rule.id ?? crypto.randomUUID(),
          id: rule.id,
          bindingId: rule.bindingId,
          inputs: {},
        };
      }),
    })),
  };
}

function toFieldInputs(
  fields: EditableField[],
  _ruleDefinitions: RuleDefinitionSummary[],
): BusinessObjectFieldDefinitionInput[] {
  return fields.map((field) => ({
    id: field.id,
    fieldKey: field.fieldKey.trim(),
    label: field.label.trim(),
    fieldType: field.fieldType,
    choiceConfiguration:
      field.fieldType === 'Choice'
        ? {
            selectionMode: field.choiceSelectionMode,
            options: field.options.map((option) => ({
              id: option.id,
              optionKey: option.optionKey.trim(),
              label: option.label.trim(),
            })),
          }
        : undefined,
    rules: field.rules.map((rule) => {
      if (!rule.bindingId) {
        throw new Error('A rule binding must be created before the object is saved.');
      }
      return {
        id: rule.id,
        bindingId: rule.bindingId,
      };
    }),
  }));
}

async function ensureRuleBindings(
  fields: EditableField[],
  ruleDefinitions: RuleDefinitionSummary[],
  objectName: string,
): Promise<EditableField[]> {
  if (validateRuleBindings(fields, ruleDefinitions).length > 0) {
    throw new Error('Rule bindings must be valid before they are created.');
  }
  const objectKey = deriveKey(objectName);
  const definitionsByKey = new Map(
    ruleDefinitions.map((definition) => [definition.definitionKey, definition] as const),
  );
  const nextFields: EditableField[] = [];
  for (const field of fields) {
    const nextRules: AppliedRule[] = [];
    for (const [ruleIndex, rule] of field.rules.entries()) {
      if (rule.bindingId) {
        nextRules.push(rule);
        continue;
      }
      if (!rule.definitionKey || !rule.definitionVersion) {
        throw new Error('A rule definition and version are required before saving.');
      }
      const inputMappings: Record<
        string,
        | { kind: 'Context'; contextKey: 'record.value'; literalValues: [] }
        | { kind: 'Literal'; contextKey: null; literalValues: string[] }
      > = {};
      const definition = definitionsByKey.get(rule.definitionKey);
      if (definition?.inputs?.some((input) => input.key === 'value')) {
        inputMappings.value = {
          kind: 'Context',
          contextKey: 'record.value',
          literalValues: [],
        };
      }
      for (const [key, values] of Object.entries(rule.inputs)) {
        if (key === 'value') continue;
        const type = definition?.inputs?.find((input) => input.key === key)?.types?.[0];
        const literalValues = values
          .filter((value) => value.trim())
          .map((value) => toRuleContractValue(value, type));
        if (literalValues.length > 0) {
          inputMappings[key] = { kind: 'Literal', contextKey: null, literalValues };
        }
      }
      const binding = await createRuleBinding({
        definitionKey: rule.definitionKey,
        definitionVersion: rule.definitionVersion,
        targetType: 'business-object-field',
        targetId: `${objectKey}.${field.fieldKey.trim()}`,
        useCaseOrTrigger: 'field-validation',
        inputMappings,
        priority: ruleIndex,
        enabled: true,
        failureBehavior: 'FailClosed',
      });
      if (!binding.id) throw new Error('The rule binding response did not include an ID.');
      nextRules.push({ ...rule, bindingId: binding.id });
    }
    nextFields.push({ ...field, rules: nextRules });
  }
  return nextFields;
}

function isCompatibleRule(definition: RuleDefinitionSummary, field: EditableField): boolean {
  if (!definition.definitionKey || !definition.activeVersion) return false;
  const valueInput = definition.inputs?.find((input) => input.key === 'value');
  const valueType = field.fieldType === 'Choice' ? 'Text' : field.fieldType;
  return (
    valueInput?.types?.includes(valueType) === true &&
    (field.fieldType !== 'Choice' ||
      field.choiceSelectionMode !== 'Multiple' ||
      valueInput.allowMultiple === true)
  );
}

type RuleBindingIssue =
  | { kind: 'incompatible'; rule: AppliedRule; definition?: RuleDefinitionSummary }
  | { kind: 'requiredLiteral'; rule: AppliedRule; input: RuleInput };

function validateRuleBindings(
  fields: EditableField[],
  ruleDefinitions: RuleDefinitionSummary[],
): RuleBindingIssue[] {
  const definitionsByKey = new Map(
    ruleDefinitions.map((definition) => [definition.definitionKey, definition] as const),
  );
  const issues: RuleBindingIssue[] = [];
  for (const field of fields) {
    for (const rule of field.rules) {
      if (rule.bindingId) continue;
      const definition = definitionsByKey.get(rule.definitionKey);
      if (!definition || !isCompatibleRule(definition, field)) {
        issues.push({ kind: 'incompatible', rule, definition });
        continue;
      }
      for (const input of definition.inputs ?? []) {
        if (
          input.key !== 'value' &&
          input.isRequired &&
          !(rule.inputs[input.key ?? ''] ?? []).some((value) => value.trim())
        ) {
          issues.push({ kind: 'requiredLiteral', rule, input });
        }
      }
    }
  }
  return issues;
}

function ruleInputErrorKey(ruleId: string, inputKey: string): string {
  return `${ruleId}:${inputKey}`;
}

function toRuleInputErrors(
  issues: RuleBindingIssue[],
  locale: string,
  t: ReturnType<typeof useTranslation>['t'],
): RuleInputErrors {
  return Object.fromEntries(
    issues.flatMap((issue) =>
      issue.kind === 'requiredLiteral'
        ? [
            [
              ruleInputErrorKey(issue.rule.clientId, issue.input.key ?? ''),
              formatRuleIssue(issue, locale, t),
            ],
          ]
        : [],
    ),
  );
}

function formatRuleIssue(
  issue: RuleBindingIssue,
  locale: string,
  t: ReturnType<typeof useTranslation>['t'],
): string {
  if (issue.kind === 'requiredLiteral') {
    return t('businessObjects.ruleInputRequired', { input: ruleInputLabel(issue.input, t) });
  }
  return t('businessObjects.ruleNoLongerCompatible', {
    rule: ruleDisplayName(
      issue.definition ?? { definitionKey: issue.rule.definitionKey },
      locale,
      t,
    ),
  });
}

function ruleInputLabel(input: RuleInput, t: ReturnType<typeof useTranslation>['t']): string {
  return input.label?.trim() || humanize(input.key ?? '') || t('rules.unnamedParameter');
}

function toRuleContractValue(value: string, type?: RuleInputValueType): string {
  return type === 'DateTime' && value ? new Date(value).toISOString() : value.trim();
}

function ruleDisplayName(
  definition: RuleDefinitionSummary,
  locale: string,
  t: ReturnType<typeof useTranslation>['t'],
): string {
  const fallback = definition.name || definition.definitionKey || t('businessObjects.unknownRule');
  return referenceContent(definition.documentation, locale)?.displayName ?? fallback;
}

function parameterInputType(
  type?: RuleInputValueType,
): 'text' | 'number' | 'date' | 'datetime-local' {
  if (type === 'Integer' || type === 'Decimal') return 'number';
  if (type === 'Date') return 'date';
  if (type === 'DateTime') return 'datetime-local';
  return 'text';
}

function deriveKey(value: string): string {
  const normalized = value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/đ/g, 'd')
    .toLocaleLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');
  const key = /^[a-z]/.test(normalized) ? normalized : normalized ? `object_${normalized}` : '';
  return key.slice(0, 63).replace(/_+$/g, '');
}

function humanize(value: string): string {
  return value.replace(/[_-]+/g, ' ').replace(/\b\w/g, (character) => character.toUpperCase());
}

function cacheDefinition(
  queryClient: ReturnType<typeof useQueryClient>,
  definition: BusinessObjectDefinitionDetail,
) {
  if (definition.id) {
    queryClient.setQueryData(businessObjectDefinitionQueryKeys.detail(definition.id), definition);
  }
}

async function invalidateLists(queryClient: ReturnType<typeof useQueryClient>) {
  await queryClient.invalidateQueries({ queryKey: businessObjectDefinitionQueryKeys.lists() });
}

function readApiError(
  error: unknown,
  fallback: string,
  actionUnavailable: string,
  temporarilyUnavailable: string,
): string {
  if (error instanceof ApiError) {
    if (error.status === 403 || error.status === 404) return actionUnavailable;
    if (error.status === 503) return temporarilyUnavailable;
  }
  if (!(error instanceof ApiError) || typeof error.data !== 'object' || error.data === null) {
    return fallback;
  }
  const detail = (error.data as { detail?: unknown }).detail;
  return typeof detail === 'string' && detail ? detail : fallback;
}
