import { Children, isValidElement, type ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type { RuleLogicalOperator } from '../api';

export function RuleConditionTree({
  operator,
  operatorLabel,
  onOperatorClick,
  children,
}: {
  operator: RuleLogicalOperator;
  operatorLabel: string;
  onOperatorClick?: () => void;
  children: ReactNode;
}) {
  const branches = Children.toArray(children);
  const parallel = operator === 'Any';
  const inverted = operator === 'Not';

  return (
    <div
      role="group"
      aria-label={operatorLabel}
      data-slot="rule-condition-group"
      data-operator={operator}
      className="relative w-fit max-w-full"
    >
      {parallel ? (
        <>
          <span
            aria-hidden="true"
            data-slot="rule-condition-parallel-rail"
            data-edge="inline-start"
            className="absolute top-3 bottom-3 left-3 w-px bg-border"
          />
          <span
            aria-hidden="true"
            data-slot="rule-condition-parallel-rail"
            data-edge="inline-end"
            className="absolute top-3 right-3 bottom-3 w-px bg-border"
          />
        </>
      ) : inverted ? (
        <>
          <span
            aria-hidden="true"
            data-slot="rule-condition-inversion-line"
            className="absolute top-3 left-0 h-px w-6 bg-border"
          />
          <span
            aria-hidden="true"
            data-slot="rule-condition-inversion"
            className="absolute top-3 left-3 size-2.5 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-muted-foreground bg-background"
          />
        </>
      ) : (
        <span
          aria-hidden="true"
          data-slot="rule-condition-serial-rail"
          className="absolute top-3 bottom-3 left-3 w-px bg-border"
        />
      )}
      {onOperatorClick ? (
        <Button
          type="button"
          variant="ghost"
          size="icon-xs"
          title={operatorLabel}
          aria-label={operatorLabel}
          data-slot="rule-condition-operator"
          className="absolute inset-y-0 left-0 z-10 h-auto w-6 p-0"
          onClick={onOperatorClick}
        />
      ) : null}
      <ul className="relative flex w-fit max-w-full flex-col gap-2">
        {branches.map((branch, index) => (
          <li
            key={isValidElement(branch) ? branch.key : index}
            data-slot="rule-condition-item"
            className={cn('relative min-h-6 list-none pl-6', parallel && 'pr-6')}
          >
            {parallel ? (
              <>
                <span
                  aria-hidden="true"
                  data-slot="rule-condition-parallel-branch"
                  data-edge="inline-start"
                  className="absolute top-3 left-3 h-px w-3 bg-border"
                />
                <span
                  aria-hidden="true"
                  data-slot="rule-condition-parallel-branch"
                  data-edge="inline-end"
                  className="absolute top-3 right-3 h-px w-3 bg-border"
                />
              </>
            ) : inverted ? null : (
              <>
                <span
                  aria-hidden="true"
                  data-slot="rule-condition-serial-branch"
                  className="absolute top-3 left-3 h-px w-3 bg-border"
                />
                <span
                  aria-hidden="true"
                  data-slot="rule-condition-serial-node"
                  className="absolute top-3 left-3 size-1.5 -translate-x-1/2 -translate-y-1/2 rounded-full bg-muted-foreground"
                />
              </>
            )}
            <div className="min-h-6 min-w-0">{branch}</div>
          </li>
        ))}
      </ul>
    </div>
  );
}
