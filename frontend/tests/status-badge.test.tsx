import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { StatusBadge } from '@/components/shared/StatusBadge';

describe('StatusBadge', () => {
  it('maps durable state meaning to one shared semantic treatment', () => {
    render(
      <>
        <StatusBadge state="informative">Built-in</StatusBadge>
        <StatusBadge state="positive">Published</StatusBadge>
        <StatusBadge state="caution">Partially installed</StatusBadge>
        <StatusBadge state="critical">Failed</StatusBadge>
        <StatusBadge state="neutral">Draft</StatusBadge>
        <StatusBadge state="inactive">Archived</StatusBadge>
      </>,
    );

    expect(screen.getByText('Built-in')).toHaveClass('border-info/25', 'bg-info/10', 'text-info');
    expect(screen.getByText('Published')).toHaveClass(
      'border-success/25',
      'bg-success/10',
      'text-success',
    );
    expect(screen.getByText('Partially installed')).toHaveClass(
      'border-warning/25',
      'bg-warning/10',
      'text-warning',
    );
    expect(screen.getByText('Failed')).toHaveClass(
      'border-destructive/25',
      'bg-destructive/10',
      'text-destructive',
    );
    expect(screen.getByText('Draft')).toHaveAttribute('data-variant', 'secondary');
    expect(screen.getByText('Archived')).toHaveClass('bg-muted/50', 'text-muted-foreground');

    expect(screen.getByText('Failed')).toHaveAttribute('data-status-state', 'critical');
  });
});
