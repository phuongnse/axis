import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { BrandHeader } from '../src/components/shared/BrandHeader';

describe('BrandHeader', () => {
  it('uses open spacing instead of a separator before card content', () => {
    const { container } = render(<BrandHeader label="Sign in" labelElement="h1" />);

    expect(screen.getByRole('heading', { name: 'Sign in', level: 1 })).toBeInTheDocument();
    expect(container.firstElementChild).toHaveClass('flex', 'items-center', 'gap-3', 'pb-2');
    expect(container.firstElementChild).not.toHaveClass('space-y-6');
    expect(screen.queryByRole('separator')).not.toBeInTheDocument();
  });
});
