import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useTranslation } from 'react-i18next';
import { describe, expect, it } from 'vitest';

import { PreferenceControls, PreferenceEffects } from '../src/features/preferences';

function PreferenceProbe() {
  const { t } = useTranslation();

  return (
    <>
      <PreferenceEffects />
      <PreferenceControls />
      <p>{t('landing.heroBody')}</p>
    </>
  );
}

describe('Preferences', () => {
  it('switches language immediately and persists the selection', async () => {
    const user = userEvent.setup();
    render(<PreferenceProbe />);

    await user.click(screen.getByRole('button', { name: /switch language to vietnamese/i }));

    expect(
      await screen.findByText(
        'Quản lý mô hình dữ liệu, quy trình và không gian làm việc ở một nơi.',
      ),
    ).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('vi');
    expect(window.localStorage.getItem('axis.language')).toBe('vi');
  });

  it('switches theme mode immediately and persists the selection', async () => {
    const user = userEvent.setup();
    render(<PreferenceProbe />);

    await user.click(screen.getByRole('button', { name: /switch theme to dark/i }));

    await waitFor(() => {
      expect(document.documentElement).toHaveClass('dark');
    });
    expect(document.documentElement.dataset.themeMode).toBe('dark');
    expect(window.localStorage.getItem('axis.theme')).toBe('dark');

    await user.click(screen.getByRole('button', { name: /switch theme to light/i }));

    await waitFor(() => {
      expect(document.documentElement).not.toHaveClass('dark');
    });
    expect(document.documentElement.dataset.themeMode).toBe('light');
    expect(window.localStorage.getItem('axis.theme')).toBe('light');
  });
});
