import { useTranslation } from 'react-i18next';
import { Badge } from '@/components/ui/badge';
import type { RuleConditionNode, RuleOperand } from '../api';

export function RuleExpressionView({ condition }: { condition: RuleConditionNode }) {
  return (
    <div data-slot="rule-expression" className="rounded-md border bg-muted/25 p-3">
      <ConditionNodeView node={condition} />
    </div>
  );
}

function ConditionNodeView({ node }: { node: RuleConditionNode }) {
  const { t } = useTranslation();
  if (node.logicalOperator) {
    return (
      <div className="space-y-2">
        <Badge variant="outline">{t(`rules.group${node.logicalOperator}`)}</Badge>
        <div className="space-y-2 border-l-2 border-border pl-3">
          {(node.children ?? []).map((child, index) => (
            <ConditionNodeView
              key={child.nodeId ?? `${node.nodeId ?? 'group'}-${index}`}
              node={child}
            />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-wrap items-baseline gap-x-2 gap-y-1 text-sm leading-6 text-foreground">
      {node.left ? <OperandView operand={node.left} /> : <span>—</span>}
      <span className="font-medium text-muted-foreground">
        {node.predicateOperator
          ? t(`rules.operator${node.predicateOperator}`)
          : t('rules.unknownOperator')}
      </span>
      {node.right ? <OperandView operand={node.right} /> : null}
    </div>
  );
}

function OperandView({ operand }: { operand: RuleOperand }) {
  const { t } = useTranslation();
  if (operand.kind === 'Function') {
    const label = operand.function
      ? t(`rules.function${operand.function}`, { defaultValue: humanize(operand.function) })
      : t('rules.unknownFunction');
    return (
      <span className="font-medium">
        {label}
        <span aria-hidden>(</span>
        <FunctionArgumentsView arguments={operand.arguments ?? []} />
        <span aria-hidden>)</span>
      </span>
    );
  }
  if (operand.kind === 'Context') {
    return (
      <span className="font-medium">
        {operand.reference === 'field.value'
          ? t('rules.contextFieldValue')
          : humanize(operand.reference ?? t('rules.contextField'))}
      </span>
    );
  }
  if (operand.kind === 'Parameter') {
    return <span className="font-medium">{operand.reference || t('rules.unnamedParameter')}</span>;
  }
  return (
    <span className="rounded bg-background px-1.5 py-0.5 font-mono text-xs font-medium">
      {(operand.literal?.values ?? []).join(', ') || '—'}
    </span>
  );
}

function FunctionArgumentsView({ arguments: operands }: { arguments: RuleOperand[] }) {
  const [first, ...remaining] = operands;
  if (!first) return null;
  return (
    <>
      <OperandView operand={first} />
      {remaining.length > 0 ? (
        <>
          <span aria-hidden>, </span>
          <FunctionArgumentsView arguments={remaining} />
        </>
      ) : null}
    </>
  );
}

function humanize(value: string): string {
  return value
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/[._]/g, ' ')
    .replace(/^./, (character) => character.toUpperCase());
}
