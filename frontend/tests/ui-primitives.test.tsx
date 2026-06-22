import { render, screen } from '@testing-library/react';
import { Search } from 'lucide-react';
import { describe, expect, it } from 'vitest';

import { CheckboxField } from '../src/components/ui/checkbox-field';
import { IconButton } from '../src/components/ui/icon-button';
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
