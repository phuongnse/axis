import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { Alert, AlertDescription, AlertTitle } from '../src/components/ui/alert';
import { Badge } from '../src/components/ui/badge';
import { Button } from '../src/components/ui/button';
import { Card, CardFooter } from '../src/components/ui/card';
import { Checkbox } from '../src/components/ui/checkbox';
import { Field, FieldDescription, FieldError, FieldLabel } from '../src/components/ui/field';
import { Input } from '../src/components/ui/input';
import { InputGroup, InputGroupInput } from '../src/components/ui/input-group';
import { Item } from '../src/components/ui/item';
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
import { Tabs, TabsList, TabsTrigger } from '../src/components/ui/tabs';
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
    expect(checkbox).toHaveClass('disabled:opacity-50');
    expect(screen.getByText('Required').closest('label')).toHaveClass('peer-disabled:opacity-50');
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

  it('uses the default and destructive shadcn treatments', () => {
    render(
      <>
        <Alert>
          <AlertTitle>Information</AlertTitle>
        </Alert>
        <Alert variant="destructive">
          <AlertTitle>Cannot save</AlertTitle>
        </Alert>
      </>,
    );

    expect(screen.getByText('Information').closest('[role="alert"]')).toHaveClass('bg-card');
    expect(screen.getByText('Cannot save').closest('[role="alert"]')).toHaveClass(
      'text-destructive',
    );
  });
});

describe('Button', () => {
  it('uses the default shadcn variants and sizes', () => {
    render(
      <>
        <Button>Default</Button>
        <Button variant="outline" size="sm">
          Outline
        </Button>
        <Button variant="destructive">Destructive</Button>
        <Button variant="link">Link</Button>
      </>,
    );

    expect(screen.getByRole('button', { name: 'Default' })).toHaveClass('bg-primary');
    expect(screen.getByRole('button', { name: 'Outline' })).toHaveClass('border-border', 'h-7');
    expect(screen.getByRole('button', { name: 'Destructive' })).toHaveClass('text-destructive');
    expect(screen.getByRole('button', { name: 'Link' })).toHaveClass('hover:underline');
  });
});

describe('Badge', () => {
  it('uses the default shadcn variants', () => {
    render(
      <>
        <Badge>Default</Badge>
        <Badge variant="secondary">Secondary</Badge>
        <Badge variant="destructive">Destructive</Badge>
        <Badge variant="outline">Outline</Badge>
      </>,
    );

    expect(screen.getByText('Default')).toHaveAttribute('data-variant', 'default');
    expect(screen.getByText('Secondary')).toHaveAttribute('data-variant', 'secondary');
    expect(screen.getByText('Destructive')).toHaveAttribute('data-variant', 'destructive');
    expect(screen.getByText('Outline')).toHaveAttribute('data-variant', 'outline');
  });
});

describe('Tabs', () => {
  it('uses the default active-tab treatment', () => {
    render(
      <Tabs defaultValue="rules">
        <TabsList>
          <TabsTrigger value="rules">Rules</TabsTrigger>
        </TabsList>
      </Tabs>,
    );

    expect(screen.getByRole('tablist')).toHaveAttribute('data-variant', 'default');
    expect(screen.getByRole('tab', { name: 'Rules' })).toHaveClass('data-active:bg-background');
  });
});

describe('Card', () => {
  it('renders caller-owned card content', () => {
    render(<Card>All checks are current.</Card>);

    expect(screen.getByText('All checks are current.')).toBeInTheDocument();
  });

  it('uses the default ring treatment', () => {
    render(<Card>All checks are current.</Card>);

    expect(screen.getByText('All checks are current.')).toHaveClass('ring-1');
  });

  it('supports the default and small sizes', () => {
    render(
      <>
        <Card>Default card</Card>
        <Card size="sm">Small card</Card>
      </>,
    );

    expect(screen.getByText('Default card')).toHaveAttribute('data-size', 'default');
    expect(screen.getByText('Small card')).toHaveAttribute('data-size', 'sm');
  });

  it('uses the default footer layout', () => {
    render(
      <Card>
        <CardFooter>Footer actions</CardFooter>
      </Card>,
    );

    expect(screen.getByText('Footer actions')).toHaveClass('flex', 'items-center');
  });
});

describe('Item', () => {
  it('uses the default outline treatment', () => {
    render(
      <Item variant="outline" size="xs" render={<Button type="button" />}>
        Customer
      </Item>,
    );

    expect(screen.getByRole('button', { name: 'Customer' })).toHaveAttribute(
      'data-variant',
      'outline',
    );
    expect(screen.getByRole('button', { name: 'Customer' })).toHaveClass('border-border');
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
      'group-data-[disabled=true]/field:opacity-50',
    );
    expect(screen.getByLabelText('Locked name')).toHaveClass('disabled:opacity-50');
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

    expect(screen.getByLabelText('Readonly key')).toHaveAttribute('readonly');
    expect(screen.getByLabelText('Readonly key')).toHaveClass('bg-transparent');
    expect(screen.getByLabelText('Disabled key')).toHaveClass(
      'disabled:bg-input/50',
      'disabled:opacity-50',
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
      'has-disabled:bg-input/50',
      'has-disabled:opacity-50',
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
  it('anchors the menu to the trigger instead of the selected item by default', async () => {
    const user = userEvent.setup();

    render(
      <>
        <Label htmlFor="positioned-field-type">Field type</Label>
        <Select defaultValue="decimal">
          <SelectTrigger id="positioned-field-type">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="text">Text</SelectItem>
            <SelectItem value="integer">Integer</SelectItem>
            <SelectItem value="decimal">Decimal</SelectItem>
          </SelectContent>
        </Select>
      </>,
    );

    await user.click(screen.getByLabelText('Field type'));

    expect(screen.getByRole('listbox').closest('[data-slot="select-content"]')).toHaveAttribute(
      'data-align-trigger',
      'true',
    );
  });

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
    expect(trigger).toHaveClass('disabled:opacity-50');
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
      <ToggleGroup aria-label="Preferences" defaultValue={['language']} orientation="vertical">
        <ToggleGroupItem value="language">Language</ToggleGroupItem>
      </ToggleGroup>,
    );

    expect(container.querySelector('[data-slot="toggle-group"]')).toHaveClass(
      'data-vertical:items-stretch',
    );
    expect(screen.getByRole('button', { name: 'Language' })).toHaveClass('shrink-0');
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
      'disabled:bg-input/50',
      'disabled:opacity-50',
    );
  });
});

describe('Skeleton', () => {
  it('renders the shadcn skeleton slot', () => {
    render(<Skeleton data-testid="loading-line" />);

    expect(screen.getByTestId('loading-line')).toHaveAttribute('data-slot', 'skeleton');
  });
});
