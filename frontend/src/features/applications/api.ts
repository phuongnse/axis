import { queryOptions } from '@tanstack/react-query';
import {
  createBusinessObjectDefinition,
  getBusinessObjectDefinition,
  listBusinessObjectDefinitions,
  publishBusinessObjectDefinition,
  saveUnpublishedBusinessObjectDefinition,
} from '@/features/business-objects';
import { createRuleBinding, listRuleBindingUsage, type RuleBindingUsage } from '@/features/rules';
import { fetchApi } from '@/lib/api';
import type * as ApiTypes from '@/lib/api-generated';

export type BusinessObjectRecordDetail = ApiTypes.BusinessObjectRecordDetailDto;
export type BusinessObjectRecordListItem = ApiTypes.BusinessObjectRecordListItemDto;
export type BusinessObjectRecordPage = ApiTypes.BusinessObjectRecordListItemDtoPagedResult;
export type BusinessObjectRecordField = ApiTypes.BusinessObjectRecordFieldContractDto;
export type BusinessObjectRecordRuleEvaluation = ApiTypes.BusinessObjectRecordRuleEvaluationDto;
export type BusinessObjectFieldType = ApiTypes.BusinessObjectFieldType;
export type BusinessObjectDefinitionDetail = ApiTypes.BusinessObjectDefinitionDetailDto;

export const sampleApplicationObjectKey = 'loan_application';

export const applicationQueryKeys = {
  all: ['applications'] as const,
  definitions: () => [...applicationQueryKeys.all, 'definitions'] as const,
  records: () => [...applicationQueryKeys.all, 'records'] as const,
  lists: () => [...applicationQueryKeys.records(), 'list'] as const,
  list: (page: number, pageSize: number, objectKey: string) =>
    [...applicationQueryKeys.lists(), page, pageSize, objectKey] as const,
  detail: (recordId: string) => [...applicationQueryKeys.records(), 'detail', recordId] as const,
};

export function applicationRecordsQueryOptions(
  page = 1,
  pageSize = 20,
  objectKey = sampleApplicationObjectKey,
) {
  return queryOptions({
    queryKey: applicationQueryKeys.list(page, pageSize, objectKey),
    queryFn: ({ signal }) => listBusinessObjectRecords(page, pageSize, objectKey, signal),
  });
}

export function applicationRecordDetailQueryOptions(recordId: string) {
  return queryOptions({
    queryKey: applicationQueryKeys.detail(recordId),
    queryFn: () => getBusinessObjectRecord(recordId),
  });
}

export async function listBusinessObjectRecords(
  page: number,
  pageSize: number,
  objectKey = sampleApplicationObjectKey,
  signal?: AbortSignal,
): Promise<BusinessObjectRecordPage> {
  const search = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
    objectKey,
  });
  return fetchApi<BusinessObjectRecordPage>(`/business-object-records?${search.toString()}`, {
    signal,
  });
}

export async function createBusinessObjectRecord(
  objectKey: string,
  request: ApiTypes.CreateBusinessObjectRecordRequest,
): Promise<BusinessObjectRecordDetail> {
  return fetchApi<BusinessObjectRecordDetail>(
    `/business-object-records/${encodeURIComponent(objectKey)}`,
    { method: 'POST', body: JSON.stringify(request) },
  );
}

export async function getBusinessObjectRecord(
  recordId: string,
): Promise<BusinessObjectRecordDetail> {
  return fetchApi<BusinessObjectRecordDetail>(`/business-object-records/${recordId}`);
}

export async function saveBusinessObjectRecord(
  recordId: string,
  request: ApiTypes.SaveBusinessObjectRecordRequest,
): Promise<BusinessObjectRecordDetail> {
  return fetchApi<BusinessObjectRecordDetail>(`/business-object-records/${recordId}`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export async function submitBusinessObjectRecord(
  recordId: string,
  request: ApiTypes.SubmitBusinessObjectRecordRequest,
): Promise<ApiTypes.BusinessObjectRecordSubmitResultDto> {
  return fetchApi<ApiTypes.BusinessObjectRecordSubmitResultDto>(
    `/business-object-records/${recordId}/submit`,
    { method: 'POST', body: JSON.stringify(request) },
  );
}

export async function findSampleApplicationDefinition(): Promise<BusinessObjectDefinitionDetail | null> {
  const page = await listBusinessObjectDefinitions(1, 100, sampleApplicationObjectKey, 'en');
  const item = page.items?.find((candidate) => candidate.objectKey === sampleApplicationObjectKey);
  return item?.id ? getBusinessObjectDefinition(item.id) : null;
}

export async function provisionSampleApplication(): Promise<BusinessObjectDefinitionDetail> {
  let definition = await findSampleApplicationDefinition();
  if (!definition) {
    definition = await createBusinessObjectDefinition({ name: 'Loan application' });
  }

  const requiredBindingId = await ensureSampleBinding('field.required', 'applicant_name', {
    value: contextMapping(),
  });
  const emailBindingId = await ensureSampleBinding('field.text_format', 'contact_email', {
    value: contextMapping(),
    format: literalMapping(['Email']),
  });
  const amountBindingId = await ensureSampleBinding('field.numeric_range', 'requested_amount', {
    value: contextMapping(),
    min: literalMapping(['1000']),
    max: literalMapping(['50000']),
  });

  if (definition.status === 'Published') return definition;

  definition = await saveUnpublishedBusinessObjectDefinition(definition.id ?? '', {
    expectedRevision: definition.revision ?? 1,
    name: definition.name ?? 'Loan application',
    fields: [
      {
        fieldKey: 'applicant_name',
        label: 'Applicant name',
        fieldType: 'Text',
        rules: [{ bindingId: requiredBindingId }],
      },
      {
        fieldKey: 'contact_email',
        label: 'Contact email',
        fieldType: 'Text',
        rules: [{ bindingId: emailBindingId }],
      },
      {
        fieldKey: 'requested_amount',
        label: 'Requested amount',
        fieldType: 'Integer',
        rules: [{ bindingId: amountBindingId }],
      },
      {
        fieldKey: 'purpose',
        label: 'Purpose',
        fieldType: 'Text',
        rules: [],
      },
    ],
  });

  return publishBusinessObjectDefinition(definition.id ?? '', {
    expectedRevision: definition.revision ?? 1,
  });
}

async function ensureSampleBinding(
  definitionKey: string,
  fieldKey: string,
  inputMappings: Record<string, ApiTypes.RuleInputMappingDto>,
): Promise<string> {
  const usages: RuleBindingUsage[] = await listRuleBindingUsage(definitionKey, 1);
  const existing = usages.find(
    (usage) =>
      usage.targetType === 'business-object-field' &&
      usage.targetId === `${sampleApplicationObjectKey}.${fieldKey}` &&
      usage.useCaseOrTrigger === 'field-validation' &&
      usage.enabled !== false,
  );
  if (existing?.bindingId) return existing.bindingId;

  const binding = await createRuleBinding({
    definitionKey,
    definitionVersion: 1,
    targetType: 'business-object-field',
    targetId: `${sampleApplicationObjectKey}.${fieldKey}`,
    useCaseOrTrigger: 'field-validation',
    inputMappings,
    failureBehavior: 'FailClosed',
  });
  return binding.id ?? '';
}

function contextMapping(): ApiTypes.RuleInputMappingDto {
  return { kind: 'Context', contextKey: 'record.value', literalValues: [] };
}

function literalMapping(values: string[]): ApiTypes.RuleInputMappingDto {
  return { kind: 'Literal', contextKey: null, literalValues: values };
}
