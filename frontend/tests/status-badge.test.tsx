import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { StatusBadge } from '@/components/shared/StatusBadge';

describe('StatusBadge', () => {
  it('maps classification and lifecycle emphasis to semantic tones', () => {
    render(
      <>
        <StatusBadge tone="brand">Workspace</StatusBadge>
        <StatusBadge tone="info">Built-in</StatusBadge>
        <StatusBadge tone="success">Published</StatusBadge>
        <StatusBadge tone="neutral">Draft</StatusBadge>
        <StatusBadge tone="muted">Archived</StatusBadge>
      </>,
    );

    expect(screen.getByText('Workspace')).toHaveClass(
      'border-primary/25',
      'bg-primary/10',
      'text-primary',
    );
    expect(screen.getByText('Built-in')).toHaveClass('border-info/25', 'bg-info/10', 'text-info');
    expect(screen.getByText('Published')).toHaveClass(
      'border-success/25',
      'bg-success/10',
      'text-success',
    );
    expect(screen.getByText('Draft')).toHaveAttribute('data-variant', 'secondary');
    expect(screen.getByText('Archived')).toHaveClass('bg-muted/50', 'text-muted-foreground');
  });
});
