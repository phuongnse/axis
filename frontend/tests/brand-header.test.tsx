import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { BrandHeader } from '../src/components/shared/BrandHeader';
import { axisStyles } from '../src/theme.generated';

describe('BrandHeader', () => {
  it('uses open spacing instead of a separator before card content', () => {
    const { container } = render(<BrandHeader label="Sign in" labelElement="h1" />);

    expect(screen.getByRole('heading', { name: 'Sign in', level: 1 })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Sign in', level: 1 })).toHaveClass(
      'font-heading',
      axisStyles.typography.scale.pageTitle,
      axisStyles.typography.weight.pageTitle,
      'text-foreground',
    );
    expect(container.firstElementChild).toHaveClass(
      'flex',
      'items-center',
      axisStyles.spacing.gap.region,
      axisStyles.spacing.padding.bottom.inline,
    );
    expect(container.firstElementChild).not.toHaveClass('space-y-6');
    expect(screen.queryByRole('separator')).not.toBeInTheDocument();
  });
});
