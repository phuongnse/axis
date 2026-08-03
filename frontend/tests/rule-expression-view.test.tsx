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

function notGroup(child: RuleExpressionDisplayNode): RuleExpressionDisplayNode {
  return {
    nodeId: `not-${child.nodeId}`,
    tokens: [
      {
        text: 'This must not match',
        referenceKind: 'LogicalOperator',
        referenceKey: 'Not',
      },
    ],
    children: [child],
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

  it('preserves nested All, Any, and Not grouping semantics', () => {
    const { container } = render(
      <RuleExpressionView
        display={allGroup([
          anyGroup([
            predicate('first', 'Status is Draft'),
            predicate('second', 'Status is Pending'),
          ]),
          notGroup(predicate('third', 'Status is Archived')),
        ])}
      />,
    );

    expect(screen.getByText('or')).toBeInTheDocument();
    expect(screen.getByText('and')).toBeInTheDocument();
    expect(screen.getByText('not')).toBeInTheDocument();
    expect(container.querySelectorAll('[data-slot="rule-expression-operator"]')).toHaveLength(3);
    expect(container.querySelector('[data-operator="All"]')).toBeInTheDocument();
    expect(container.querySelector('[data-operator="Any"]')).toBeInTheDocument();
    expect(container.querySelector('[data-operator="Not"]')).toBeInTheDocument();
  });

  it('renders optional-bound assertions as conditional expressions', () => {
    const { container } = render(
      <RuleExpressionView
        display={allGroup([
          predicate(
            'minimum-bound',
            'Value is greater than or equal to Minimum when Minimum is specified',
          ),
          predicate(
            'maximum-bound',
            'Value is less than or equal to Maximum when Maximum is specified',
          ),
        ])}
      />,
    );

    expect(container.querySelectorAll('[data-slot="rule-expression-operator"]')).toHaveLength(1);
    expect(screen.queryByText('not')).not.toBeInTheDocument();
    expect(
      screen.getByText('Value is greater than or equal to Minimum when Minimum is specified'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Value is less than or equal to Maximum when Maximum is specified'),
    ).toBeInTheDocument();
  });
});
