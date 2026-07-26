import { referenceContent, referenceLabel } from '@/lib/reference-metadata';
import type {
  RuleExpressionCardinality,
  RuleExpressionLanguage,
  RuleOperand,
  RuleValueType,
} from './api';

export function operandKindReference(
  language: RuleExpressionLanguage | undefined,
  kind: RuleOperand['kind'],
  locale: string,
) {
  return referenceContent(
    language?.operandKinds?.find((definition) => definition.kind === kind)?.documentation,
    locale,
  );
}

export function valueTypeLabel(
  language: RuleExpressionLanguage | undefined,
  type: RuleValueType | undefined,
  locale: string,
) {
  const documentation = language?.valueTypes?.find(
    (definition) => definition.type === type,
  )?.documentation;
  return referenceLabel(documentation, locale, type);
}

export function cardinalityLabel(
  language: RuleExpressionLanguage | undefined,
  cardinality: RuleExpressionCardinality | undefined,
  locale: string,
) {
  const documentation = language?.cardinalities?.find(
    (definition) => definition.cardinality === cardinality,
  )?.documentation;
  return referenceLabel(documentation, locale, cardinality);
}
