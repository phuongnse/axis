import { useTranslation } from 'react-i18next';
import type { RuleExpressionDisplayNode, RuleLogicalOperator } from '../api';

export function RuleExpressionView({ display }: { display: RuleExpressionDisplayNode }) {
  const { t } = useTranslation();

  if (!isValidRuleExpressionDisplay(display)) {
    return (
      <p role="alert" className="text-sm text-destructive">
        {t('rules.loadErrorTitle')}
      </p>
    );
  }

  return (
    <div data-slot="rule-expression">
      <DisplayNodeView node={display} />
    </div>
  );
}

function DisplayNodeView({ node }: { node: RuleExpressionDisplayNode }) {
  const { t } = useTranslation();
  const children = node.children ?? [];
  const logicalToken = (node.tokens ?? []).find(
    (token) => token.referenceKind === 'LogicalOperator',
  );
  const logicalOperator = logicalOperatorFrom(logicalToken?.referenceKey);
  const content = (
    <span className="text-sm leading-6 text-foreground">
      {(node.tokens ?? []).map((token, index, tokens) => {
        const text = token.text ?? '';
        const spacer = displayTokenSpacer(index, text, tokens[index - 1]?.text ?? '');
        const tokenClassName = token.isCode ? 'font-mono text-xs font-medium' : undefined;
        return (
          // biome-ignore lint/suspicious/noArrayIndexKey: Projection tokens are immutable and do not have independent identity.
          <span key={`${index}:${text}`}>
            {spacer}
            <span className={tokenClassName ?? 'font-medium'}>{text}</span>
          </span>
        );
      })}
    </span>
  );

  if (children.length === 0) {
    return <div className="min-h-6">{content}</div>;
  }

  if (!logicalOperator) {
    throw new Error('Rule logical group projection is invalid.');
  }

  const onlyChild = children.length === 1 ? children[0] : undefined;
  if (onlyChild && logicalOperator !== 'Not') {
    return <DisplayNodeView node={onlyChild} />;
  }

  const separator =
    logicalOperator === 'All'
      ? t('rules.logicAnd')
      : logicalOperator === 'Any'
        ? t('rules.logicOr')
        : t('rules.logicNot');
  const directPredicates = children.every((child) => (child.children ?? []).length === 0);

  if (logicalOperator === 'Not') {
    return (
      <div
        data-slot="rule-expression-group"
        data-operator={logicalOperator}
        className="flex min-w-0 flex-wrap items-baseline gap-x-2 gap-y-1"
      >
        <LogicalSeparator label={separator} accessibleLabel={logicalToken?.text ?? separator} />
        {children.map((child, index) => (
          <DisplayNodeView
            key={child.nodeId ?? `${node.nodeId ?? 'display'}-${index}`}
            node={child}
          />
        ))}
      </div>
    );
  }

  return (
    <div
      data-slot="rule-expression-group"
      data-operator={logicalOperator}
      className={
        directPredicates
          ? 'flex min-w-0 flex-wrap items-baseline gap-x-2 gap-y-1'
          : 'min-w-0 space-y-1'
      }
    >
      {children.map((child, index) => (
        <div
          key={child.nodeId ?? `${node.nodeId ?? 'display'}-${index}`}
          className={directPredicates ? 'contents' : undefined}
        >
          {index > 0 ? (
            <LogicalSeparator label={separator} accessibleLabel={logicalToken?.text ?? separator} />
          ) : null}
          <DisplayNodeView node={child} />
        </div>
      ))}
    </div>
  );
}

function LogicalSeparator({ label, accessibleLabel }: { label: string; accessibleLabel: string }) {
  const className =
    'h-auto p-0 text-xs font-semibold uppercase tracking-wide text-muted-foreground';
  return (
    <span data-slot="rule-expression-operator" title={accessibleLabel} className={className}>
      {label}
    </span>
  );
}

function logicalOperatorFrom(value: string | null | undefined): RuleLogicalOperator | null {
  return value === 'All' || value === 'Any' || value === 'Not' ? value : null;
}

function isValidRuleExpressionDisplay(node: RuleExpressionDisplayNode): boolean {
  const children = node.children ?? [];
  if (children.length === 0) return true;
  const logicalToken = (node.tokens ?? []).find(
    (token) => token.referenceKind === 'LogicalOperator',
  );
  return (
    logicalOperatorFrom(logicalToken?.referenceKey) !== null &&
    children.every(isValidRuleExpressionDisplay)
  );
}

function displayTokenSpacer(index: number, text: string, previous: string): string {
  return index > 0 && text !== ')' && text !== ',' && previous !== '(' ? ' ' : '';
}
