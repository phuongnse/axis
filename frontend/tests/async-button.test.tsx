import { render, screen } from '@testing-library/react';
import { Save } from 'lucide-react';
import { describe, expect, it } from 'vitest';
import { AsyncButton } from '@/components/shared/AsyncButton';

describe('AsyncButton', () => {
  it('mirrors authoritative pending immediately while keeping its label stable', () => {
    const { rerender } = render(
      <AsyncButton icon={<Save />} pending={false} pendingLabel="Saving">
        Save
      </AsyncButton>,
    );

    const button = screen.getByRole('button', { name: 'Save' });
    const iconSlot = button.querySelector('[data-slot="async-button-icon"]');
    expect(iconSlot?.querySelector('.lucide-save')).not.toBeNull();

    rerender(
      <AsyncButton icon={<Save />} pending pendingLabel="Saving">
        Save
      </AsyncButton>,
    );
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-busy', 'true');
    expect(button).toHaveAccessibleName('Save');
    expect(button).toHaveTextContent('Save');
    expect(screen.getByRole('status')).toHaveTextContent('Saving');
    expect(iconSlot?.querySelector('[data-slot="spinner"]')).not.toBeNull();

    rerender(
      <AsyncButton icon={<Save />} pending={false} pendingLabel="Saving">
        Save
      </AsyncButton>,
    );
    expect(button).not.toBeDisabled();
    expect(button).not.toHaveAttribute('aria-busy');
    expect(iconSlot?.querySelector('.lucide-save')).not.toBeNull();
  });
});
