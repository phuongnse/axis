import { render, screen } from '@testing-library/react';
import { Inbox, Search } from 'lucide-react';
import { describe, expect, it } from 'vitest';

import { ActionLink } from '../src/components/ui/action-link';
import { Badge } from '../src/components/ui/badge';
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '../src/components/ui/card';
import { CheckboxField } from '../src/components/ui/checkbox-field';
import { ContentGrid } from '../src/components/ui/content-grid';
import { EmptyState } from '../src/components/ui/empty-state';
import { FormField } from '../src/components/ui/form-field';
import { IconButton } from '../src/components/ui/icon-button';
import { Input } from '../src/components/ui/input';
import { Label } from '../src/components/ui/label';
import { Notice } from '../src/components/ui/notice';
import { PageHeader } from '../src/components/ui/page-header';
import { Panel } from '../src/components/ui/panel';
import { Progress } from '../src/components/ui/progress';
import { Select } from '../src/components/ui/select';
import { Skeleton } from '../src/components/ui/skeleton';
import { Textarea } from '../src/components/ui/textarea';
import { Toolbar } from '../src/components/ui/toolbar';

import { renderWithRouter } from './render-with-router';

describe('ActionLink', () => {
  it('renders a routed call to action with an accessible name', async () => {
    await renderWithRouter(
      <ActionLink to="/login" icon={Search}>
        Sign in
      </ActionLink>,
    );

    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/login');
  });
});

describe('IconButton', () => {
  it('renders an accessible icon-only command', () => {
    render(<IconButton type="button" icon={Search} label="Search records" />);

    expect(screen.getByRole('button', { name: 'Search records' })).toBeInTheDocument();
  });

  it('uses loading state as the accessible name and disables interaction', () => {
    render(
      <IconButton
        type="button"
        icon={Search}
        label="Search records"
        isLoading
        loadingLabel="Searching records"
      />,
    );

    const button = screen.getByRole('button', { name: 'Searching records' });
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-busy', 'true');
  });
});

describe('Textarea', () => {
  it('passes standard textarea state attributes through', () => {
    render(<Textarea aria-label="Notes" aria-invalid defaultValue="Design notes" />);

    const textarea = screen.getByLabelText('Notes');
    expect(textarea).toHaveValue('Design notes');
    expect(textarea).toHaveAttribute('aria-invalid', 'true');
  });
});

describe('CheckboxField', () => {
  it('links validation copy to the checkbox control', () => {
    render(
      <CheckboxField id="terms" error="Accept terms before continuing.">
        Terms
      </CheckboxField>,
    );

    const checkbox = screen.getByLabelText('Terms');
    expect(checkbox).toHaveAttribute('aria-invalid', 'true');
    expect(checkbox).toHaveAccessibleDescription('Accept terms before continuing.');
  });
});

describe('Notice', () => {
  it('uses alert semantics for warning and error feedback', () => {
    render(
      <Notice variant="warning" title="Needs review">
        Check the required fields.
      </Notice>,
    );

    expect(screen.getByRole('alert')).toHaveTextContent('Needs review');
  });

  it('uses status semantics for informational feedback', () => {
    render(
      <Notice variant="success" title="Ready">
        Workspace is active.
      </Notice>,
    );

    expect(screen.getByRole('status')).toHaveTextContent('Workspace is active.');
  });
});

describe('Badge', () => {
  it('renders compact status text', () => {
    render(<Badge variant="success">Stable</Badge>);

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

    expect(screen.getByRole('heading', { name: 'Workspace health' })).toBeInTheDocument();
    expect(screen.getByText('All checks are current.')).toBeInTheDocument();
  });
});

describe('ContentGrid', () => {
  it('renders grouped content without changing child semantics', () => {
    render(
      <ContentGrid>
        <section aria-label="Models">Models</section>
        <section aria-label="Forms">Forms</section>
      </ContentGrid>,
    );

    expect(screen.getByRole('region', { name: 'Models' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Forms' })).toBeInTheDocument();
  });
});

describe('FormField', () => {
  it('links help and error copy to its control', () => {
    render(
      <FormField
        id="workspace-name"
        label="Workspace name"
        helpText="Shown in navigation"
        error="Required"
      >
        {({ describedBy }) => (
          <Input id="workspace-name" aria-describedby={describedBy} aria-invalid />
        )}
      </FormField>,
    );

    const input = screen.getByLabelText('Workspace name');
    expect(input).toHaveAttribute('aria-describedby', 'workspace-name-help workspace-name-error');
    expect(screen.getByText('Shown in navigation')).toBeInTheDocument();
    expect(screen.getByText('Required')).toBeInTheDocument();
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

describe('PageHeader', () => {
  it('renders the page title, support copy, and actions', () => {
    render(
      <PageHeader
        eyebrow="Workspace"
        title="Dashboard"
        description="Track active workflows."
        actions={<span>Refresh</span>}
      />,
    );

    expect(screen.getByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
    expect(screen.getByText('Track active workflows.')).toBeInTheDocument();
    expect(screen.getByText('Refresh')).toBeInTheDocument();
  });
});

describe('Panel', () => {
  it('renders caller-owned semantics and content', () => {
    render(
      <Panel role="region" aria-label="Usage">
        Current usage is within limits.
      </Panel>,
    );

    expect(screen.getByRole('region', { name: 'Usage' })).toHaveTextContent(
      'Current usage is within limits.',
    );
  });
});

describe('Progress', () => {
  it('keeps a determinate value accessible', () => {
    render(<Progress value={42} aria-label="Storage used" />);

    const progress = screen.getByLabelText('Storage used');
    expect(progress).toHaveAttribute('value', '42');
    expect(progress).toHaveAttribute('max', '100');
  });

  it('omits value for indeterminate progress semantics', () => {
    render(<Progress isIndeterminate value={42} aria-label="Syncing workspace" />);

    const progress = screen.getByLabelText('Syncing workspace');
    expect(progress).not.toHaveAttribute('value');
    expect(progress).toHaveAttribute('max', '100');
  });
});

describe('Select', () => {
  it('passes native select state attributes through', () => {
    render(
      <Select aria-label="Environment" aria-invalid defaultValue="production">
        <option value="production">Production</option>
        <option value="sandbox">Sandbox</option>
      </Select>,
    );

    const select = screen.getByLabelText('Environment');
    expect(select).toHaveValue('production');
    expect(select).toHaveAttribute('aria-invalid', 'true');
  });
});

describe('Skeleton', () => {
  it('stays hidden from assistive technology', () => {
    render(<Skeleton data-testid="loading-line" />);

    expect(screen.getByTestId('loading-line')).toHaveAttribute('aria-hidden', 'true');
  });
});

describe('Toolbar', () => {
  it('supports caller-owned toolbar semantics', () => {
    render(
      <Toolbar role="toolbar" aria-label="View actions">
        <span>Filter</span>
        <span>Sort</span>
      </Toolbar>,
    );

    expect(screen.getByRole('toolbar', { name: 'View actions' })).toHaveTextContent('Filter');
  });
});

describe('EmptyState', () => {
  it('renders a user-actionable empty state', () => {
    render(
      <EmptyState
        icon={Inbox}
        title="No records yet"
        description="Create the first record when the model is ready."
      />,
    );

    expect(screen.getByRole('heading', { name: 'No records yet' })).toBeInTheDocument();
    expect(
      screen.getByText('Create the first record when the model is ready.'),
    ).toBeInTheDocument();
  });
});
