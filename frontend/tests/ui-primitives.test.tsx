import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { Alert, AlertDescription, AlertTitle } from '../src/components/ui/alert';
import { Card } from '../src/components/ui/card';
import { Checkbox } from '../src/components/ui/checkbox';
import { Field, FieldDescription, FieldError, FieldLabel } from '../src/components/ui/field';
import { Input } from '../src/components/ui/input';
import { InputGroup, InputGroupInput } from '../src/components/ui/input-group';
import { Label } from '../src/components/ui/label';
import { Popover, PopoverContent, PopoverTrigger } from '../src/components/ui/popover';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '../src/components/ui/select';
import { Skeleton } from '../src/components/ui/skeleton';
import { Textarea } from '../src/components/ui/textarea';
import { ToggleGroup, ToggleGroupItem } from '../src/components/ui/toggle-group';

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

  it('uses visible disabled affordances for checkbox controls and labels', () => {
    render(
      <Label>
        <Checkbox disabled />
        Required
      </Label>,
    );

    const checkbox = screen.getByRole('checkbox', { name: 'Required' });
    expect(checkbox).toHaveAttribute('aria-disabled', 'true');
    expect(checkbox).toHaveAttribute('data-disabled');
    expect(checkbox).toHaveClass(
      'data-disabled:border-border',
      'data-disabled:bg-muted',
      'data-disabled:text-muted-foreground',
      'data-disabled:opacity-100',
      'dark:data-disabled:border-foreground/30',
      'dark:data-disabled:bg-accent',
    );
    expect(screen.getByText('Required').closest('label')).toHaveClass(
      'has-data-disabled:text-muted-foreground',
    );
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

  it('uses visible background treatments for default and destructive alerts', () => {
    render(
      <>
        <Alert>
          <AlertTitle>Needs review</AlertTitle>
        </Alert>
        <Alert variant="destructive">
          <AlertTitle>Cannot save</AlertTitle>
        </Alert>
      </>,
    );

    const [defaultAlert, destructiveAlert] = screen.getAllByRole('alert');

    expect(defaultAlert).toHaveClass('bg-muted/70');
    expect(destructiveAlert).toHaveClass('bg-destructive/15');
  });
});

describe('Card', () => {
  it('renders caller-owned card content', () => {
    render(<Card>All checks are current.</Card>);

    expect(screen.getByText('All checks are current.')).toBeInTheDocument();
  });

  it('uses a continuous border treatment', () => {
    render(<Card>All checks are current.</Card>);

    expect(screen.getByText('All checks are current.')).toHaveClass('border');
    expect(screen.getByText('All checks are current.')).toHaveClass('border-border/80');
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

  it('keeps disabled field labels and controls visually explicit', () => {
    render(
      <Field>
        <FieldLabel htmlFor="locked-name">Locked name</FieldLabel>
        <Input id="locked-name" disabled />
      </Field>,
    );

    expect(screen.getByText('Locked name')).toHaveClass(
      'group-has-disabled/field:text-muted-foreground',
      'group-has-disabled/field:opacity-100',
    );
    expect(screen.getByLabelText('Locked name')).toHaveClass(
      'disabled:border-border',
      'disabled:bg-muted',
      'disabled:text-muted-foreground',
      'disabled:opacity-100',
    );
  });
});

describe('Input', () => {
  it('uses distinct read-only and disabled visual states', () => {
    render(
      <>
        <Label htmlFor="readonly-key">Readonly key</Label>
        <Input id="readonly-key" readOnly />
        <Label htmlFor="disabled-key">Disabled key</Label>
        <Input id="disabled-key" disabled />
      </>,
    );

    expect(screen.getByLabelText('Readonly key')).toHaveClass(
      'read-only:bg-muted/60',
      'read-only:text-muted-foreground',
      'dark:read-only:border-foreground/25',
      'dark:read-only:bg-accent/80',
    );
    expect(screen.getByLabelText('Disabled key')).toHaveClass(
      'disabled:bg-muted',
      'disabled:text-muted-foreground',
      'disabled:opacity-100',
      'dark:disabled:border-foreground/30',
      'dark:disabled:bg-accent',
    );
  });
});

describe('InputGroup', () => {
  it('shows disabled state on the shared group container', () => {
    render(
      <InputGroup data-testid="input-group">
        <InputGroupInput aria-label="Grouped search" disabled />
      </InputGroup>,
    );

    expect(screen.getByTestId('input-group')).toHaveClass(
      'has-disabled:border-border',
      'has-disabled:bg-muted',
      'has-disabled:text-muted-foreground',
      'dark:has-disabled:border-foreground/30',
      'dark:has-disabled:bg-accent',
    );
    expect(screen.getByLabelText('Grouped search')).toHaveClass('disabled:bg-transparent');
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

describe('Popover', () => {
  it('opens shadcn popover content from its trigger', async () => {
    const user = userEvent.setup();

    render(
      <Popover>
        <PopoverTrigger>Open preferences</PopoverTrigger>
        <PopoverContent>Language choices</PopoverContent>
      </Popover>,
    );

    await user.click(screen.getByRole('button', { name: 'Open preferences' }));

    expect(screen.getByText('Language choices')).toBeInTheDocument();
  });
});

describe('Select', () => {
  it('links a select trigger to its label', () => {
    render(
      <>
        <Label htmlFor="field-type">Field type</Label>
        <Select defaultValue="short-text">
          <SelectTrigger id="field-type">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="short-text">Short text</SelectItem>
          </SelectContent>
        </Select>
      </>,
    );

    expect(screen.getByLabelText('Field type')).toHaveAttribute('data-slot', 'select-trigger');
  });

  it('uses visible disabled affordances on the trigger', () => {
    render(
      <>
        <Label htmlFor="locked-field-type">Field type</Label>
        <Select defaultValue="short-text" disabled>
          <SelectTrigger id="locked-field-type">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="short-text">Short text</SelectItem>
          </SelectContent>
        </Select>
      </>,
    );

    const trigger = screen.getByLabelText('Field type');
    expect(trigger).toHaveAttribute('data-disabled');
    expect(trigger).toHaveClass(
      'disabled:bg-muted',
      'data-disabled:bg-muted',
      'disabled:text-muted-foreground',
      'data-disabled:text-muted-foreground',
      'disabled:opacity-100',
      'data-disabled:opacity-100',
      'dark:disabled:border-foreground/30',
      'dark:disabled:bg-accent',
      'dark:data-disabled:border-foreground/30',
      'dark:data-disabled:bg-accent',
    );
  });
});

describe('ToggleGroup', () => {
  it('updates the active item through user interaction', async () => {
    const user = userEvent.setup();

    render(
      <ToggleGroup aria-label="View mode" defaultValue={['list']}>
        <ToggleGroupItem value="list">List</ToggleGroupItem>
        <ToggleGroupItem value="grid">Grid</ToggleGroupItem>
      </ToggleGroup>,
    );

    expect(screen.getByRole('button', { name: 'List' })).toHaveAttribute('aria-pressed', 'true');

    await user.click(screen.getByRole('button', { name: 'Grid' }));

    expect(screen.getByRole('button', { name: 'Grid' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('supports start-aligned full-width toggle items through the component API', () => {
    const { container } = render(
      <ToggleGroup aria-label="Preferences" defaultValue={['language']} className="w-full">
        <ToggleGroupItem value="language" className="w-full justify-start">
          Language
        </ToggleGroupItem>
      </ToggleGroup>,
    );

    expect(container.querySelector('[data-slot="toggle-group"]')).toHaveClass('w-full');
    expect(screen.getByRole('button', { name: 'Language' })).toHaveClass('justify-start');
    expect(screen.getByRole('button', { name: 'Language' })).toHaveClass('w-full');
  });
});

describe('Textarea', () => {
  it('links a textarea control to its label', () => {
    render(
      <>
        <Label htmlFor="options">Options</Label>
        <Textarea id="options" defaultValue="active=Active" />
      </>,
    );

    expect(screen.getByLabelText('Options')).toHaveValue('active=Active');
    expect(screen.getByLabelText('Options')).toHaveAttribute('data-slot', 'textarea');
  });

  it('uses visible disabled affordances', () => {
    render(
      <>
        <Label htmlFor="locked-options">Options</Label>
        <Textarea id="locked-options" disabled />
      </>,
    );

    expect(screen.getByLabelText('Options')).toHaveClass(
      'disabled:bg-muted',
      'disabled:text-muted-foreground',
      'disabled:opacity-100',
      'dark:disabled:border-foreground/30',
      'dark:disabled:bg-accent',
    );
  });
});

describe('Skeleton', () => {
  it('renders the shadcn skeleton slot', () => {
    render(<Skeleton data-testid="loading-line" />);

    expect(screen.getByTestId('loading-line')).toHaveAttribute('data-slot', 'skeleton');
  });
});
