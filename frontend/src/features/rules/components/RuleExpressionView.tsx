import { useState } from 'react';
import { Button } from '@/components/ui/button';
import type {
  RuleContextSchema,
  RuleExpressionDisplayNode,
  RuleLogicalOperator,
  RuleParameterDefinition,
} from '../api';
import { RuleConditionTree } from './RuleConditionTree';
import { RuleExpressionGuide, type RuleExpressionGuideTarget } from './RuleExpressionGuide';

export function RuleExpressionView({
  display,
  definitionKey,
  expressionLanguageVersion,
  contextSchema,
  parameters = [],
  references = false,
}: {
  display: RuleExpressionDisplayNode;
  definitionKey?: string;
  expressionLanguageVersion?: number;
  contextSchema?: RuleContextSchema;
  parameters?: RuleParameterDefinition[];
  references?: boolean;
}) {
  const [target, setTarget] = useState<RuleExpressionGuideTarget | null>(null);

  return (
    <>
      <div data-slot="rule-expression">
        <DisplayNodeView node={display} references={references} onReference={setTarget} />
      </div>
      <RuleExpressionGuide
        expressionLanguageVersion={expressionLanguageVersion}
        definitionKey={definitionKey}
        contextSchema={contextSchema}
        parameters={parameters}
        target={target}
        open={target !== null}
        trigger={false}
        onOpenChange={(open) => !open && setTarget(null)}
      />
    </>
  );
}

function DisplayNodeView({
  node,
  references,
  onReference,
}: {
  node: RuleExpressionDisplayNode;
  references: boolean;
  onReference: (selection: RuleExpressionGuideTarget) => void;
}) {
  const children = node.children ?? [];
  const logicalToken = (node.tokens ?? []).find(
    (token) => token.referenceKind === 'LogicalOperator',
  );
  const logicalOperator = logicalOperatorFrom(logicalToken?.referenceKey);
  const logicalSelection = logicalToken
    ? displayTokenTarget(
        logicalToken.referenceKind,
        logicalToken.referenceKey,
        logicalToken.text ?? '',
      )
    : null;
  const content = (
    <span className="text-sm leading-6 text-foreground">
      {(node.tokens ?? []).map((token, index, tokens) => {
        const text = token.text ?? '';
        const spacer = displayTokenSpacer(index, text, tokens[index - 1]?.text ?? '');
        const selection = displayTokenTarget(token.referenceKind, token.referenceKey, text);
        const tokenClassName =
          token.referenceKind === 'Parameter' || token.referenceKind === 'Context' || token.isCode
            ? 'font-mono text-xs font-medium'
            : undefined;
        return (
          // biome-ignore lint/suspicious/noArrayIndexKey: Projection tokens are immutable and do not have independent identity.
          <span key={`${index}:${text}`}>
            {spacer}
            {references && selection ? (
              <Button
                type="button"
                variant="link"
                size="xs"
                className="inline-flex h-auto p-0 font-medium underline decoration-dotted underline-offset-4"
                onClick={() => onReference(selection)}
              >
                <span className={tokenClassName}>{text}</span>
              </Button>
            ) : (
              <span className={tokenClassName ?? 'font-medium'}>{text}</span>
            )}
          </span>
        );
      })}
    </span>
  );

  if (children.length > 0 && !logicalOperator) {
    throw new Error('Rule logical group projection is invalid.');
  }

  return children.length > 0 ? (
    <RuleConditionTree
      operator={logicalOperator!}
      operatorLabel={logicalToken?.text ?? ''}
      onOperatorClick={
        references && logicalSelection ? () => onReference(logicalSelection) : undefined
      }
    >
      {children.map((child, index) => (
        <DisplayNodeView
          key={child.nodeId ?? `${node.nodeId ?? 'display'}-${index}`}
          node={child}
          references={references}
          onReference={onReference}
        />
      ))}
    </RuleConditionTree>
  ) : (
    <div className="min-h-6">{content}</div>
  );
}

function logicalOperatorFrom(value: string | null | undefined): RuleLogicalOperator | null {
  return value === 'All' || value === 'Any' || value === 'Not' ? value : null;
}

function displayTokenTarget(
  kind: NonNullable<RuleExpressionDisplayNode['tokens']>[number]['referenceKind'],
  key: string | null | undefined,
  displayText: string,
): RuleExpressionGuideTarget | null {
  if (!kind || !key || !displayText) return null;
  return {
    referenceKind: kind === 'Literal' ? 'ValueType' : kind,
    referenceKey: key,
    displayText,
  };
}

function displayTokenSpacer(index: number, text: string, previous: string): string {
  return index > 0 && text !== ')' && text !== ',' && previous !== '(' ? ' ' : '';
}
