import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { ManagedDialogTabs } from '@/components/shared/ManagedDialogTabs';

describe('ManagedDialogTabs', () => {
  it('orders General, business sections, and system information with line-tab semantics', async () => {
    const user = userEvent.setup();
    render(
      <ManagedDialogTabs
        label="Definition sections"
        generalLabel="General"
        general={<p>General content</p>}
        sections={[
          { id: 'behavior', label: 'Behavior', content: <p>Behavior content</p> },
          { id: 'usage', label: 'Usage', content: <p>Usage content</p> },
        ]}
        systemInfo={{ label: 'System info', content: <p>System content</p> }}
      />,
    );

    const tablist = screen.getByRole('tablist', { name: 'Definition sections' });
    expect(tablist).toHaveAttribute('data-variant', 'line');
    expect(
      within(tablist)
        .getAllByRole('tab')
        .map((tab) => tab.textContent),
    ).toEqual(['General', 'Behavior', 'Usage', 'System info']);
    expect(tablist.parentElement).toHaveAttribute('data-slot', 'managed-dialog-tab-scroll');
    expect(tablist.parentElement).toHaveClass('sm:overflow-x-clip');
    expect(tablist.parentElement).toHaveClass('overflow-y-hidden');

    await user.click(screen.getByRole('tab', { name: 'System info' }));
    expect(screen.getByText('System content')).toBeVisible();
  });

  it('keeps panel state mounted while switching sections', async () => {
    const user = userEvent.setup();
    render(
      <ManagedDialogTabs
        label="Definition sections"
        generalLabel="General"
        general={<input aria-label="Name" />}
        sections={[{ id: 'fields', label: 'Fields', content: <p>Fields content</p> }]}
      />,
    );

    await user.type(screen.getByLabelText('Name'), 'Customer');
    await user.click(screen.getByRole('tab', { name: 'Fields' }));
    await user.click(screen.getByRole('tab', { name: 'General' }));

    expect(screen.getByLabelText('Name')).toHaveValue('Customer');
  });

  it('omits the tablist when General is the only available section', () => {
    render(
      <ManagedDialogTabs
        label="Definition sections"
        generalLabel="General"
        general={<p>General content</p>}
      />,
    );

    expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
    expect(screen.getByText('General content')).toBeVisible();
  });
});
