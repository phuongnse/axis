import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { StatusBadge } from '@/components/shared/StatusBadge';

describe('StatusBadge', () => {
  it('maps lifecycle emphasis to semantic tones', () => {
    render(
      <>
        <StatusBadge tone="success">Published</StatusBadge>
        <StatusBadge tone="neutral">Draft</StatusBadge>
        <StatusBadge tone="muted">Archived</StatusBadge>
      </>,
    );

    expect(screen.getByText('Published')).toHaveClass(
      'border-success/25',
      'bg-success/10',
      'text-success',
    );
    expect(screen.getByText('Draft')).toHaveAttribute('data-variant', 'secondary');
    expect(screen.getByText('Archived')).toHaveClass('bg-muted/50', 'text-muted-foreground');
  });
});
