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

describe('Button', () => {
  it('owns inline, account-menu, and destructive-menu treatments', () => {
    render(
      <>
        <Button variant="link" size="inline">
          Inline action
        </Button>
        <Button variant="outline" size="account">
          Account menu
        </Button>
        <Button variant="destructiveOutline" size="menu">
          Sign out
        </Button>
      </>,
    );

    expect(screen.getByRole('button', { name: 'Inline action' })).toHaveAttribute(
      'data-size',
      'inline',
    );
    expect(screen.getByRole('button', { name: 'Account menu' })).toHaveAttribute(
      'data-size',
      'account',
    );
    expect(screen.getByRole('button', { name: 'Sign out' })).toHaveAttribute(
      'data-variant',
      'destructiveOutline',
    );
  });
});

describe('Badge', () => {
  it('owns primary, success, and warning outline treatments', () => {
    render(
      <>
        <Badge variant="primaryOutline">Ready</Badge>
        <Badge variant="successOutline">Published</Badge>
        <Badge variant="warningOutline">Not published</Badge>
      </>,
    );

    expect(screen.getByText('Ready')).toHaveClass(
      'border-primary/20',
      'bg-primary/10',
      'text-primary',
    );
    expect(screen.getByText('Published')).toHaveClass(
      'border-emerald-600/30',
      'bg-emerald-500/15',
      'text-emerald-800',
    );
    expect(screen.getByText('Not published')).toHaveClass(
      'border-amber-500/35',
      'bg-amber-400/20',
      'text-amber-900',
    );
  });
});

describe('Tabs', () => {
  it('owns the soft active-tab treatment', () => {
    render(
      <Tabs defaultValue="rules">
        <TabsList variant="soft">
          <TabsTrigger value="rules">Rules</TabsTrigger>
        </TabsList>
      </Tabs>,
    );

    expect(screen.getByRole('tablist')).toHaveAttribute('data-variant', 'soft');
    expect(screen.getByRole('tab', { name: 'Rules' })).toHaveClass(
      'group-data-[variant=soft]/tabs-list:data-active:border-transparent',
      'group-data-[variant=soft]/tabs-list:data-active:bg-chart-1/25',
      'group-data-[variant=soft]/tabs-list:data-active:font-semibold',
      'group-data-[variant=soft]/tabs-list:data-active:text-foreground',
      'group-data-[variant=soft]/tabs-list:data-active:shadow-none',
      'group-data-[variant=soft]/tabs-list:data-active:[&_svg]:text-chart-1',
    );
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

  it('owns large, flush, and destructive surface treatments', () => {
    render(
      <>
        <Card size="lg">Large card</Card>
        <Card size="flush">Flush card</Card>
        <Card variant="destructive">Destructive card</Card>
      </>,
    );

    expect(screen.getByText('Large card')).toHaveAttribute('data-size', 'lg');
    expect(screen.getByText('Flush card')).toHaveAttribute('data-size', 'flush');
    expect(screen.getByText('Destructive card')).toHaveAttribute('data-variant', 'destructive');
  });

  it('owns vertical footer layout', () => {
    render(
      <Card>
        <CardFooter orientation="vertical">Footer actions</CardFooter>
      </Card>,
    );

    expect(screen.getByText('Footer actions')).toHaveAttribute('data-orientation', 'vertical');
    expect(screen.getByText('Footer actions')).toHaveClass('flex-col', 'items-stretch', 'gap-3');
  });
});

describe('Item', () => {
  it('owns selectable navigation treatment', () => {
    render(
      <Item variant="selectable" size="xs" render={<Button type="button" />} aria-current="true">
        Customer
      </Item>,
    );

    expect(screen.getByRole('button', { name: 'Customer' })).toHaveAttribute(
      'data-variant',
      'selectable',
    );
    expect(screen.getByRole('button', { name: 'Customer' })).toHaveClass(
      'justify-start',
      'aria-current:bg-accent',
    );
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

  it('shows a required marker without changing the control label', () => {
    render(
      <Field>
        <FieldLabel htmlFor="workspace-name" required>
          Workspace name
        </FieldLabel>
        <Input id="workspace-name" required />
      </Field>,
    );

    expect(screen.getByLabelText('Workspace name')).toBeRequired();
    const label = screen.getByText('Workspace name');
    const requiredIcon = label.querySelector('[data-slot="field-required-icon"]');

    expect(label).toHaveAttribute('data-required', 'true');
    expect(label).toHaveClass('items-center', 'gap-1');
    expect(label.className).not.toContain('after:content');
    expect(requiredIcon).toBeInTheDocument();
    expect(requiredIcon).toHaveAttribute('aria-hidden', 'true');
    expect(requiredIcon).toHaveClass('size-3', 'self-center', 'text-destructive');
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

  it('owns body-style checkbox labels', () => {
    render(<FieldLabel variant="body">Agreement</FieldLabel>);

    expect(screen.getByText('Agreement')).toHaveAttribute('data-variant', 'body');
    expect(screen.getByText('Agreement')).toHaveClass('font-normal', 'leading-5');
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
      'false',
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
      <ToggleGroup aria-label="Preferences" defaultValue={['language']} orientation="vertical">
        <ToggleGroupItem value="language">Language</ToggleGroupItem>
      </ToggleGroup>,
    );

    expect(container.querySelector('[data-slot="toggle-group"]')).toHaveClass(
      'data-vertical:w-full',
    );
    expect(screen.getByRole('button', { name: 'Language' })).toHaveClass(
      'group-data-vertical/toggle-group:justify-start',
      'group-data-vertical/toggle-group:w-full',
    );
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
