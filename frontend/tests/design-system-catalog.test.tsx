import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { DesignSystemCatalog } from '../src/features/design-system';
import { renderWithRouter } from './render-with-router';

describe('DesignSystemCatalog', () => {
  it('renders the component catalog sections and primitive states', async () => {
    await renderWithRouter(<DesignSystemCatalog />, { path: '/design-system' });

    expect(screen.getByRole('heading', { name: 'Axis design system' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Semantic color tokens' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Button' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Action links' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Form controls' })).toBeInTheDocument();
    expect(screen.getByText('Primary')).toBeInTheDocument();
    expect(screen.getByText('Chart 1')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /save changes/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /disabled/i })).toBeDisabled();
    expect(screen.getByLabelText('Invalid field')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByLabelText('Accept required terms')).toBeChecked();
  });
});
