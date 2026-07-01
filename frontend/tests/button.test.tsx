import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Button } from '../src/components/ui/button';

describe('Button', () => {
  it('should render children correctly', () => {
    render(<Button>Click Me</Button>);
    expect(screen.getByText('Click Me')).toBeInTheDocument();
  });

  it('should apply default variant and size classes', () => {
    render(<Button data-testid="btn">Default</Button>);
    const button = screen.getByTestId('btn');

    expect(button).toHaveClass('inline-flex', 'items-center', 'justify-center');
    expect(button).toHaveClass('bg-primary', 'text-primary-foreground');
    expect(button).toHaveClass('h-8', 'px-2.5');
  });

  it('should apply outline variant correctly', () => {
    render(
      <Button data-testid="btn" variant="outline">
        Outline
      </Button>,
    );
    const button = screen.getByTestId('btn');
    expect(button).toHaveClass('border-border', 'bg-card');
  });

  it('should pass through standard HTML button props', () => {
    render(
      <Button data-testid="btn" disabled aria-label="test-label">
        Disabled
      </Button>,
    );
    const button = screen.getByTestId('btn');
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-label', 'test-label');
  });

  it('should render icon children with standard button semantics', () => {
    render(
      <Button data-testid="btn">
        <span aria-hidden>+</span>
        Save changes
      </Button>,
    );

    const button = screen.getByTestId('btn');
    expect(button).toHaveTextContent('Save changes');
  });
});
