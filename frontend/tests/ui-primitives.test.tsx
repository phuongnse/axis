import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { Alert, AlertDescription, AlertTitle } from '../src/components/ui/alert';
import { Card } from '../src/components/ui/card';
import { Checkbox } from '../src/components/ui/checkbox';
import { Field, FieldDescription, FieldError, FieldLabel } from '../src/components/ui/field';
import { Input } from '../src/components/ui/input';
import { Label } from '../src/components/ui/label';
import { Skeleton } from '../src/components/ui/skeleton';

describe('Checkbox', () => {
  it('links a Base UI checkbox to a label', () => {
    render(
      <>
        <Checkbox id="terms" aria-invalid />
        <Label htmlFor="terms">Terms</Label>
      </>,
    );

    const checkbox = screen.getByRole('checkbox', { name: 'Terms' });
    expect(checkbox).toHaveAttribute('aria-invalid', 'true');
  });
});

describe('Alert', () => {
  it('renders alert title and description content', () => {
    render(
      <Alert>
        <AlertTitle>Needs review</AlertTitle>
        <AlertDescription>Check the required fields.</AlertDescription>
      </Alert>,
    );

    expect(screen.getByRole('alert')).toHaveTextContent('Needs review');
    expect(screen.getByRole('alert')).toHaveTextContent('Check the required fields.');
  });
});

describe('Card', () => {
  it('keeps structured card content in reading order', () => {
    render(<Card>All checks are current.</Card>);

    expect(screen.getByText('All checks are current.')).toBeInTheDocument();
  });
});

describe('Field', () => {
  it('links description and error copy to its control', () => {
    render(
      <Field data-invalid>
        <FieldLabel htmlFor="workspace-name">Workspace name</FieldLabel>
        <Input
          id="workspace-name"
          aria-describedby="workspace-name-help workspace-name-error"
          aria-invalid
        />
        <FieldDescription id="workspace-name-help">Shown in navigation</FieldDescription>
        <FieldError id="workspace-name-error">Required</FieldError>
      </Field>,
    );

    const input = screen.getByLabelText('Workspace name');
    expect(input).toHaveAccessibleDescription('Shown in navigation Required');
    expect(input).toHaveAttribute('aria-invalid', 'true');
  });
});

describe('Label', () => {
  it('links a label to its form control', () => {
    render(
      <>
        <Label htmlFor="profile-name">Profile name</Label>
        <Input id="profile-name" />
      </>,
    );

    expect(screen.getByLabelText('Profile name')).toBeInTheDocument();
  });
});

describe('Skeleton', () => {
  it('renders the shadcn skeleton slot', () => {
    render(<Skeleton data-testid="loading-line" />);

    expect(screen.getByTestId('loading-line')).toHaveAttribute('data-slot', 'skeleton');
  });
});
