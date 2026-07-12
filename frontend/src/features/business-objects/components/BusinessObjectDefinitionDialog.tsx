import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDown, ArrowUp, Plus, Save, Trash2, UploadCloud } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
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
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
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
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { type RuleDefinitionSummary, ruleNameTranslationKey } from '@/features/rules';
import { ApiError } from '@/lib/api';
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
  definitionKey: z.string().min(1),
  definitionVersion: z.number().int().positive(),
  parameters: z.record(z.string(), z.array(z.string())),
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

interface BusinessObjectDefinitionDialogProps {
  mode?: DialogMode;
  recordId?: string;
  ruleDefinitions: RuleDefinitionSummary[];
  ruleCatalogLoading: boolean;
  ruleCatalogUnavailable: boolean;
  onCreated: (recordId: string) => void;
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
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [requestError, setRequestError] = useState<string | null>(null);
  const [discardOpen, setDiscardOpen] = useState(false);
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
  const readOnly = mode === 'view' || definition?.status === 'Published';
  const open = Boolean(mode);

  useEffect(() => {
    setRequestError(null);
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
      if (created.id) onCreated(created.id);
    },
    onError: (error) => setRequestError(readApiError(error, t('businessObjects.requestError'))),
  });

  const saveMutation = useMutation({
    mutationFn: ({
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
        fields: toFieldInputs(values.fields, ruleDefinitions),
      }),
    onSuccess: async (saved) => {
      cacheDefinition(queryClient, saved);
      form.reset(toFormValues(saved, ruleDefinitions));
      setRequestError(null);
      await invalidateLists(queryClient);
    },
    onError: (error) => setRequestError(readApiError(error, t('businessObjects.requestError'))),
  });

  const publishMutation = useMutation({
    mutationFn: ({ id, revision }: { id: string; revision: number }) =>
      publishBusinessObjectDefinition(id, { expectedRevision: revision }),
    onSuccess: async (published) => {
      cacheDefinition(queryClient, published);
      form.reset(toFormValues(published, ruleDefinitions));
      setRequestError(null);
      await invalidateLists(queryClient);
    },
    onError: (error) => setRequestError(readApiError(error, t('businessObjects.requestError'))),
  });

  const busy = createMutation.isPending || saveMutation.isPending || publishMutation.isPending;
  const title =
    mode === 'create'
      ? t('businessObjects.defineTitle')
      : definition?.name || t('businessObjects.definitionTitle');

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

  const submit = form.handleSubmit((values) => {
    setRequestError(null);
    if (mode === 'create') {
      createMutation.mutate({ name: values.name.trim() });
      return;
    }
    if (definition?.id && definition.revision != null) {
      saveMutation.mutate({ id: definition.id, values, revision: definition.revision });
    }
  });

  return (
    <>
      <Dialog
        open={open}
        onOpenChange={(nextOpen) => {
          if (!nextOpen) requestClose();
        }}
      >
        <DialogContent showCloseButton={!busy}>
          <DialogHeader>
            <DialogTitle>{title}</DialogTitle>
            <DialogDescription>
              {mode === 'create'
                ? t('businessObjects.defineDescription')
                : t('businessObjects.editorDescription')}
            </DialogDescription>
          </DialogHeader>

          <form className="contents" onSubmit={submit} noValidate>
            <div data-slot="dialog-body" className="max-h-96 min-h-0 overflow-y-auto">
              {detailQuery.isLoading && mode !== 'create' ? (
                <p role="status">{t('table.loading')}</p>
              ) : null}
              {detailQuery.isError ? (
                <Alert variant="destructive">
                  <AlertTitle>{t('businessObjects.loadError')}</AlertTitle>
                  <AlertDescription>{t('businessObjects.loadErrorDescription')}</AlertDescription>
                </Alert>
              ) : null}
              {requestError ? (
                <Alert variant="destructive">
                  <AlertTitle>{t('businessObjects.requestError')}</AlertTitle>
                  <AlertDescription>{requestError}</AlertDescription>
                </Alert>
              ) : null}

              {!detailQuery.isLoading || mode === 'create' ? (
                <Tabs defaultValue="details">
                  <TabsList aria-label={t('businessObjects.definitionSections')}>
                    <TabsTrigger value="details">{t('businessObjects.details')}</TabsTrigger>
                    {mode !== 'create' ? (
                      <TabsTrigger value="fields">{t('businessObjects.fields')}</TabsTrigger>
                    ) : null}
                    {definition?.latestPublishedVersion ? (
                      <TabsTrigger value="published">
                        {t('businessObjects.publishedVersion')}
                      </TabsTrigger>
                    ) : null}
                  </TabsList>

                  <TabsContent value="details">
                    <DefinitionDetails
                      name={name}
                      objectKey={definition?.objectKey ?? deriveKey(name)}
                      readOnly={readOnly}
                      nameError={form.formState.errors.name?.message}
                      onNameChange={(value) =>
                        form.setValue('name', value, { shouldDirty: true, shouldValidate: true })
                      }
                    />
                  </TabsContent>

                  {mode !== 'create' ? (
                    <TabsContent value="fields">
                      <FieldsEditor
                        fields={fields}
                        errors={form.formState.errors.fields}
                        readOnly={readOnly}
                        ruleDefinitions={ruleDefinitions}
                        ruleCatalogLoading={ruleCatalogLoading}
                        ruleCatalogUnavailable={ruleCatalogUnavailable}
                        onChange={updateField}
                        onMove={moveField}
                        onRemove={(index) =>
                          updateFields(fields.filter((_, fieldIndex) => fieldIndex !== index))
                        }
                        onAdd={() => updateFields([...fields, newField()])}
                      />
                    </TabsContent>
                  ) : null}

                  {definition?.latestPublishedVersion ? (
                    <TabsContent value="published">
                      <PublishedVersion definition={definition} />
                    </TabsContent>
                  ) : null}
                </Tabs>
              ) : null}
            </div>

            <DialogFooter>
              <Button type="button" variant="outline" disabled={busy} onClick={requestClose}>
                {t('app.cancel')}
              </Button>
              {mode === 'create' ? (
                <Button type="submit" disabled={busy}>
                  <Plus aria-hidden />
                  {createMutation.isPending
                    ? t('businessObjects.creating')
                    : t('businessObjects.create')}
                </Button>
              ) : null}
              {!readOnly && mode !== 'create' ? (
                <>
                  <Button
                    type="submit"
                    variant="secondary"
                    disabled={busy || !form.formState.isDirty}
                  >
                    <Save aria-hidden />
                    {saveMutation.isPending
                      ? t('businessObjects.saving')
                      : t('businessObjects.save')}
                  </Button>
                  <Button
                    type="button"
                    disabled={busy || form.formState.isDirty || fields.length === 0}
                    onClick={() => {
                      if (definition?.id && definition.revision != null) {
                        publishMutation.mutate({
                          id: definition.id,
                          revision: definition.revision,
                        });
                      }
                    }}
                  >
                    <UploadCloud aria-hidden />
                    {publishMutation.isPending
                      ? t('businessObjects.publishing')
                      : t('businessObjects.publish')}
                  </Button>
                </>
              ) : null}
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

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
    </>
  );
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
  return (
    <div className="grid gap-4 pt-4 md:grid-cols-2">
      <Field data-invalid={Boolean(nameError)}>
        <FieldLabel htmlFor="business-object-name">{t('businessObjects.name')}</FieldLabel>
        <Input
          id="business-object-name"
          value={name}
          readOnly={readOnly}
          aria-invalid={Boolean(nameError)}
          onChange={(event) => onNameChange(event.target.value)}
        />
        <FieldError>{nameError ? t(nameError) : null}</FieldError>
        <FieldDescription>{t('businessObjects.nameHelp')}</FieldDescription>
      </Field>
      <Field>
        <FieldLabel htmlFor="business-object-key">{t('businessObjects.objectKey')}</FieldLabel>
        <Input id="business-object-key" value={objectKey} readOnly aria-readonly="true" />
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
  onChange,
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
  onChange: (index: number, patch: Partial<EditableField>) => void;
  onMove: (index: number, direction: -1 | 1) => void;
  onRemove: (index: number) => void;
  onAdd: () => void;
}) {
  const { t } = useTranslation();
  return (
    <div className="pt-4">
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
                onChange={onChange}
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
  onChange,
}: {
  field: EditableField;
  index: number;
  definitions: RuleDefinitionSummary[];
  loading: boolean;
  unavailable: boolean;
  readOnly: boolean;
  onChange: (index: number, patch: Partial<EditableField>) => void;
}) {
  const { t } = useTranslation();
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
              if (!definition?.definitionKey || !definition.latestPublishedVersion) return;
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
                  {ruleDisplayName(definition, t)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        ) : null}
      </div>
      {loading ? (
        <p className="mt-3 text-sm text-muted-foreground">
          {t('businessObjects.rulesCatalogLoading')}
        </p>
      ) : null}
      {unavailable ? (
        <Alert variant="destructive" className="mt-3">
          <AlertTitle>{t('businessObjects.rulesCatalogUnavailableTitle')}</AlertTitle>
          <AlertDescription>
            {t('businessObjects.rulesCatalogUnavailableDescription')}
          </AlertDescription>
        </Alert>
      ) : null}
      <ItemGroup className="mt-3">
        {field.rules.map((rule, ruleIndex) => {
          const definition = definitions.find(
            (candidate) => candidate.definitionKey === rule.definitionKey,
          );
          return (
            <Item key={rule.clientId} variant="muted" size="sm">
              <ItemHeader>
                <ItemTitle>
                  {definition ? ruleDisplayName(definition, t) : rule.definitionKey}
                </ItemTitle>
                <ItemActions>
                  <Badge variant="outline">
                    {t('businessObjects.ruleVersion', { version: rule.definitionVersion })}
                  </Badge>
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
                {(definition?.parameters ?? []).map((parameter) => (
                  <RuleParameterEditor
                    key={parameter.key}
                    parameter={parameter}
                    values={rule.parameters[parameter.key ?? ''] ?? []}
                    readOnly={readOnly}
                    onChange={(values) =>
                      updateRule(ruleIndex, {
                        parameters: { ...rule.parameters, [parameter.key ?? '']: values },
                      })
                    }
                  />
                ))}
              </ItemContent>
            </Item>
          );
        })}
      </ItemGroup>
      {!loading && !unavailable && compatible.length === 0 && field.rules.length === 0 ? (
        <p className="mt-3 text-sm text-muted-foreground">
          {t('businessObjects.noCompatibleRules')}
        </p>
      ) : null}
    </section>
  );
}

type RuleParameter = NonNullable<RuleDefinitionSummary['parameters']>[number];

function RuleParameterEditor({
  parameter,
  values,
  readOnly,
  onChange,
}: {
  parameter: RuleParameter;
  values: string[];
  readOnly: boolean;
  onChange: (values: string[]) => void;
}) {
  const { t } = useTranslation();
  const key = parameter.key ?? '';
  const currentValues = parameter.allowMultiple
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
    <Field>
      <FieldLabel htmlFor={`rule-parameter-${key}`}>{humanize(key)}</FieldLabel>
      <div className="flex flex-col gap-2">
        {currentValues.map((value, valueIndex) => (
          <div key={valueIds[valueIndex]} className="flex items-center gap-2">
            <RuleParameterValue
              id={`rule-parameter-${key}-${valueIndex}`}
              parameter={parameter}
              value={value}
              readOnly={readOnly}
              onChange={(nextValue) =>
                onChange(
                  currentValues.map((current, index) =>
                    index === valueIndex ? nextValue : current,
                  ),
                )
              }
            />
            {!readOnly && parameter.allowMultiple && currentValues.length > 1 ? (
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
      {!readOnly && parameter.allowMultiple ? (
        <Button type="button" variant="outline" size="sm" onClick={addValue}>
          <Plus aria-hidden />
          {t('businessObjects.addParameterValue')}
        </Button>
      ) : null}
    </Field>
  );
}

function RuleParameterValue({
  id,
  parameter,
  value,
  readOnly,
  onChange,
}: {
  id: string;
  parameter: RuleParameter;
  value: string;
  readOnly: boolean;
  onChange: (value: string) => void;
}) {
  const { t } = useTranslation();
  const options =
    parameter.allowedValues?.map((allowed) => ({ value: allowed, label: allowed })) ??
    (parameter.type === 'Boolean'
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
        <SelectTrigger id={id}>
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
      type={parameterInputType(parameter.type)}
      value={value}
      readOnly={readOnly}
      onChange={(event) => onChange(event.target.value)}
    />
  );
}

function PublishedVersion({ definition }: { definition: BusinessObjectDefinitionDetail }) {
  const { t } = useTranslation();
  const version = definition.latestPublishedVersion;
  if (!version) return null;
  return (
    <div className="pt-4">
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
                <Badge variant="outline">{field.fieldKey}</Badge>
                <Badge variant="secondary">
                  {t(`businessObjects.fieldType${field.fieldType ?? 'Text'}`)}
                </Badge>
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
    definitionKey: definition.definitionKey ?? '',
    definitionVersion: definition.latestPublishedVersion ?? 1,
    parameters: Object.fromEntries(
      (definition.parameters ?? []).map((parameter) => [
        parameter.key ?? '',
        parameter.isRequired ? [''] : [],
      ]),
    ),
  };
}

function toFormValues(
  definition: BusinessObjectDefinitionDetail,
  ruleDefinitions: RuleDefinitionSummary[],
): DefinitionFormValues {
  const definitionsByKey = new Map(
    ruleDefinitions.map((rule) => [rule.definitionKey, rule] as const),
  );
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
        const definitionMetadata = definitionsByKey.get(rule.definitionKey);
        return {
          clientId: rule.id ?? crypto.randomUUID(),
          id: rule.id,
          definitionKey: rule.definitionKey ?? '',
          definitionVersion: rule.definitionVersion ?? 1,
          parameters: Object.fromEntries(
            Object.entries(rule.parameters ?? {}).map(([key, values]) => {
              const type = definitionMetadata?.parameters?.find(
                (parameter) => parameter.key === key,
              )?.type;
              return [key, values.map((value) => toEditorValue(value, type))];
            }),
          ),
        };
      }),
    })),
  };
}

function toFieldInputs(
  fields: EditableField[],
  ruleDefinitions: RuleDefinitionSummary[],
): BusinessObjectFieldDefinitionInput[] {
  const definitionsByKey = new Map(
    ruleDefinitions.map((rule) => [rule.definitionKey, rule] as const),
  );
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
      const metadata = definitionsByKey.get(rule.definitionKey);
      return {
        id: rule.id,
        definitionKey: rule.definitionKey,
        definitionVersion: rule.definitionVersion,
        parameters: Object.fromEntries(
          Object.entries(rule.parameters).map(([key, values]) => {
            const type = metadata?.parameters?.find((parameter) => parameter.key === key)?.type;
            return [
              key,
              values.filter((value) => value.trim()).map((value) => toContractValue(value, type)),
            ];
          }),
        ),
      };
    }),
  }));
}

function isCompatibleRule(definition: RuleDefinitionSummary, field: EditableField): boolean {
  if (!definition.definitionKey || !definition.latestPublishedVersion) return false;
  if (definition.origin === 'Workspace' && definition.status !== 'Published') return false;
  const targetTypes = definition.applicability?.targetTypeKeys ?? [];
  if (targetTypes.length > 0 && !targetTypes.includes(field.fieldType)) return false;
  const selectionModes = definition.applicability?.configurationConstraints?.selection_mode;
  return (
    !selectionModes ||
    field.fieldType !== 'Choice' ||
    selectionModes.includes(field.choiceSelectionMode)
  );
}

function ruleDisplayName(
  definition: RuleDefinitionSummary,
  t: ReturnType<typeof useTranslation>['t'],
): string {
  const translationKey = ruleNameTranslationKey(definition.definitionKey ?? '');
  const fallback = definition.name || definition.definitionKey || t('businessObjects.unknownRule');
  return translationKey ? t(translationKey, { defaultValue: fallback }) : fallback;
}

function parameterInputType(
  type: RuleParameter['type'],
): 'text' | 'number' | 'date' | 'datetime-local' {
  if (type === 'Integer' || type === 'Decimal') return 'number';
  if (type === 'Date') return 'date';
  if (type === 'DateTime') return 'datetime-local';
  return 'text';
}

function toEditorValue(value: string, type?: RuleParameter['type']): string {
  if (type !== 'DateTime' || !value) return value;
  const date = new Date(value);
  if (Number.isNaN(date.valueOf())) return value;
  const local = new Date(date.valueOf() - date.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}

function toContractValue(value: string, type?: RuleParameter['type']): string {
  return type === 'DateTime' && value ? new Date(value).toISOString() : value.trim();
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

function readApiError(error: unknown, fallback: string): string {
  if (!(error instanceof ApiError) || typeof error.data !== 'object' || error.data === null) {
    return fallback;
  }
  const detail = (error.data as { detail?: unknown }).detail;
  return typeof detail === 'string' && detail ? detail : fallback;
}
