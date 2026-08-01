import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { RuleExpressionDisplayNode } from '@/features/rules/api';
import { RuleExpressionView } from '@/features/rules/components/RuleExpressionView';

function predicate(nodeId: string, label: string): RuleExpressionDisplayNode {
  return {
    nodeId,
    tokens: [{ text: label, referenceKind: 'PredicateOperator', referenceKey: nodeId }],
    children: [],
  };
}

function allGroup(children: RuleExpressionDisplayNode[]): RuleExpressionDisplayNode {
  return {
    nodeId: `all-${children.map((child) => child.nodeId).join('-')}`,
    tokens: [
      {
        text: 'All conditions must match',
        referenceKind: 'LogicalOperator',
        referenceKey: 'All',
      },
    ],
    children,
  };
}

function anyGroup(
  children: RuleExpressionDisplayNode[],
  nodeId = 'any-group',
): RuleExpressionDisplayNode {
  return {
    nodeId,
    tokens: [
      {
        text: 'Any condition may match',
        referenceKind: 'LogicalOperator',
        referenceKey: 'Any',
      },
    ],
    children,
  };
}

describe('RuleExpressionView', () => {
  it('hides redundant All or Any grouping for a single condition', () => {
    const { container } = render(
      <RuleExpressionView display={allGroup([predicate('first', 'Value is provided')])} />,
    );

    expect(screen.getByText('Value is provided')).toBeInTheDocument();
    expect(screen.queryByText('All conditions must match')).not.toBeInTheDocument();
    expect(container.querySelector('[data-slot="rule-condition-group"]')).toBeNull();
  });

  it('keeps grouping semantics visible when conditions are combined', () => {
    const { container } = render(
      <RuleExpressionView
        display={allGroup([
          predicate('first', 'Value is provided'),
          predicate('second', 'Value is valid'),
        ])}
      />,
    );

    expect(screen.getByText('and')).toBeInTheDocument();
    expect(container.querySelector('[data-slot="rule-expression-group"]')).toHaveAttribute(
      'data-operator',
      'All',
    );
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('renders positive optional-bound assertions without diagram rails', () => {
    const { container } = render(
      <RuleExpressionView
        display={allGroup([
          anyGroup(
            [
              predicate('minimum-absent', 'Minimum is not provided'),
              predicate('minimum-satisfied', 'Value is greater than or equal to Minimum'),
            ],
            'minimum-bound',
          ),
          anyGroup(
            [
              predicate('maximum-absent', 'Maximum is not provided'),
              predicate('maximum-satisfied', 'Value is less than or equal to Maximum'),
            ],
            'maximum-bound',
          ),
        ])}
      />,
    );

    expect(container.querySelectorAll('[data-slot="rule-expression-operator"]')).toHaveLength(3);
    expect(screen.queryByText('not')).not.toBeInTheDocument();
    expect(screen.getByText('Value is greater than or equal to Minimum')).toBeInTheDocument();
    expect(screen.getByText('Value is less than or equal to Maximum')).toBeInTheDocument();
    expect(container.querySelector('[data-slot="rule-condition-parallel-rail"]')).toBeNull();
    expect(container.querySelector('[data-slot="rule-condition-serial-rail"]')).toBeNull();
  });
});
