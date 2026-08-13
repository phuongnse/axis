import type { ComponentProps } from 'react';

import type { StatusBadge } from '@/components/shared/StatusBadge';

type Equal<Left, Right> =
  (<Value>() => Value extends Left ? 1 : 2) extends <Value>() => Value extends Right ? 1 : 2
    ? true
    : false;
type Expect<Value extends true> = Value;

type StatusBadgeProps = ComponentProps<typeof StatusBadge>;

export type StatusBadgeUsesFiniteSemanticStates = Expect<
  Equal<
    StatusBadgeProps['state'],
    'informative' | 'positive' | 'caution' | 'critical' | 'neutral' | 'inactive'
  >
>;
