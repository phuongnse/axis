import { render, screen } from '@testing-library/react';
import { Inbox, Search } from 'lucide-react';
import { describe, expect, it } from 'vitest';

import { Badge } from '../src/components/ui/badge';
import { CheckboxField } from '../src/components/ui/checkbox-field';
import { EmptyState } from '../src/components/ui/empty-state';
import { IconButton } from '../src/components/ui/icon-button';
import { Notice } from '../src/components/ui/notice';
import { Progress } from '../src/components/ui/progress';
import { Textarea } from '../src/components/ui/textarea';

describe('IconButton', () => {
  it('renders an accessible icon-only command', () => {
    render(<IconButton type="button" icon={Search} label="Search catalog" />);

    expect(screen.getByRole('button', { name: 'Search catalog' })).toBeInTheDocument();
  });

  it('uses loading state as the accessible name and disables interaction', () => {
    render(
      <IconButton
        type="button"
        icon={Search}
        label="Search catalog"
        isLoading
        loadingLabel="Searching catalog"
      />,
    );

    const button = screen.getByRole('button', { name: 'Searching catalog' });
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

describe('Progress', () => {
  it('keeps a determinate value accessible', () => {
    render(<Progress value={42} aria-label="Storage used" />);

    const progress = screen.getByLabelText('Storage used');
    expect(progress).toHaveAttribute('value', '42');
    expect(progress).toHaveAttribute('max', '100');
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
