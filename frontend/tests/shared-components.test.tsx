import { screen } from '@testing-library/react';
import { Search } from 'lucide-react';
import { describe, expect, it } from 'vitest';

import { ActionLink } from '../src/components/shared/ActionLink';

import { renderWithRouter } from './render-with-router';

describe('ActionLink', () => {
  it('renders a routed call to action with an accessible name', async () => {
    await renderWithRouter(
      <ActionLink to="/register" icon={Search}>
        Register
      </ActionLink>,
    );

    expect(screen.getByRole('link', { name: 'Register' })).toHaveAttribute('href', '/register');
  });
});
