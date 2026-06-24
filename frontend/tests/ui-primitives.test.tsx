import { render, screen } from '@testing-library/react';
import { Inbox } from 'lucide-react';
import { describe, expect, it } from 'vitest';

import { Alert, AlertDescription, AlertTitle } from '../src/components/ui/alert';
import { Badge } from '../src/components/ui/badge';
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '../src/components/ui/card';
import { Checkbox } from '../src/components/ui/checkbox';
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from '../src/components/ui/empty';
import { Field, FieldDescription, FieldError, FieldLabel } from '../src/components/ui/field';
import { Input } from '../src/components/ui/input';
import { Label } from '../src/components/ui/label';
import { NativeSelect, NativeSelectOption } from '../src/components/ui/native-select';
import { Progress } from '../src/components/ui/progress';
import { Separator } from '../src/components/ui/separator';
import { Skeleton } from '../src/components/ui/skeleton';
import { Spinner } from '../src/components/ui/spinner';
import { Textarea } from '../src/components/ui/textarea';

describe('Textarea', () => {
  it('passes standard textarea state attributes through', () => {
    render(<Textarea aria-label="Notes" aria-invalid defaultValue="Design notes" />);

    const textarea = screen.getByLabelText('Notes');
    expect(textarea).toHaveValue('Design notes');
    expect(textarea).toHaveAttribute('aria-invalid', 'true');
  });
});

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

describe('Badge', () => {
  it('renders compact status text', () => {
    render(<Badge>Stable</Badge>);

    expect(screen.getByText('Stable')).toBeInTheDocument();
  });
});

describe('Card', () => {
  it('keeps structured card content in reading order', () => {
    render(
      <Card>
        <CardHeader>
          <CardTitle>Workspace health</CardTitle>
          <CardDescription>Recent automation status.</CardDescription>
        </CardHeader>
        <CardContent>All checks are current.</CardContent>
        <CardFooter>Updated today</CardFooter>
      </Card>,
    );

    expect(screen.getByText('Workspace health')).toBeInTheDocument();
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
        <Label htmlFor="model-name">Model name</Label>
        <Input id="model-name" />
      </>,
    );

    expect(screen.getByLabelText('Model name')).toBeInTheDocument();
  });
});

describe('Progress', () => {
  it('keeps a determinate value accessible', () => {
    render(<Progress value={42} aria-label="Storage used" />);

    const progress = screen.getByRole('progressbar', { name: 'Storage used' });
    expect(progress).toHaveAttribute('aria-valuenow', '42');
    expect(progress).toHaveAttribute('aria-valuemax', '100');
  });

  it('uses null value for indeterminate progress semantics', () => {
    render(<Progress value={null} aria-label="Syncing workspace" />);

    const progress = screen.getByRole('progressbar', { name: 'Syncing workspace' });
    expect(progress).not.toHaveAttribute('aria-valuenow');
  });
});

describe('NativeSelect', () => {
  it('passes native select state attributes through', () => {
    render(
      <NativeSelect aria-label="Environment" aria-invalid defaultValue="production">
        <NativeSelectOption value="production">Production</NativeSelectOption>
        <NativeSelectOption value="sandbox">Sandbox</NativeSelectOption>
      </NativeSelect>,
    );

    const select = screen.getByLabelText('Environment');
    expect(select).toHaveValue('production');
    expect(select).toHaveAttribute('aria-invalid', 'true');
  });
});

describe('Skeleton', () => {
  it('renders the shadcn skeleton slot', () => {
    render(<Skeleton data-testid="loading-line" />);

    expect(screen.getByTestId('loading-line')).toHaveAttribute('data-slot', 'skeleton');
  });
});

describe('Separator', () => {
  it('renders the separator role', () => {
    render(<Separator />);

    expect(screen.getByRole('separator')).toHaveAttribute('data-slot', 'separator');
  });
});

describe('Spinner', () => {
  it('announces loading status', () => {
    render(<Spinner />);

    expect(screen.getByRole('status', { name: 'Loading' })).toHaveAttribute('data-slot', 'spinner');
  });
});

describe('Empty', () => {
  it('renders a user-actionable empty state', () => {
    render(
      <Empty>
        <EmptyHeader>
          <EmptyMedia variant="icon">
            <Inbox aria-hidden />
          </EmptyMedia>
          <EmptyTitle>No records yet</EmptyTitle>
          <EmptyDescription>Create the first record when the model is ready.</EmptyDescription>
        </EmptyHeader>
      </Empty>,
    );

    expect(screen.getByText('No records yet')).toBeInTheDocument();
    expect(
      screen.getByText('Create the first record when the model is ready.'),
    ).toBeInTheDocument();
  });
});
