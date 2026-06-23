import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { DesignSystemCatalog } from '../src/features/design-system';
import { renderWithRouter } from './render-with-router';

describe('DesignSystemCatalog', () => {
  it('renders the component catalog sections and primitive states', async () => {
    await renderWithRouter(<DesignSystemCatalog />, { path: '/design-system' });

    expect(screen.getByRole('heading', { name: 'Axis design system' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Semantic color tokens' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Primitive readiness' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Consumer contracts' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Button' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Icon button' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Action links' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Form controls' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Feedback' })).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 2, name: 'Structure and data' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Layout' })).toBeInTheDocument();
    expect(screen.getAllByText('Primary').length).toBeGreaterThan(0);
    expect(screen.getAllByText('ActionLink').length).toBeGreaterThan(0);
    expect(screen.getAllByText('IconButton').length).toBeGreaterThan(0);
    expect(screen.getByText('LandingPage')).toBeInTheDocument();
    expect(screen.getByText('RegisterPage')).toBeInTheDocument();
    expect(screen.getByText('AppShell')).toBeInTheDocument();
    expect(screen.getAllByText('e2e smoke').length).toBeGreaterThan(0);
    expect(screen.getAllByText('aria busy loading').length).toBeGreaterThan(0);
    expect(screen.getByText('Chart 1')).toBeInTheDocument();
    expect(screen.getByText('Sidebar')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /save changes/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /saving/i })).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('button', { name: 'Search catalog' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Disabled' })).toBeDisabled();
    expect(screen.getByLabelText('Invalid field')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByLabelText('Notes')).toHaveValue(
      'A reusable primitive should preserve height, focus, and readable line length across dense layouts.',
    );
    expect(screen.getByLabelText('Accept required terms')).toBeChecked();
    expect(screen.getByText('Needs review')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'No records yet' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'More layout actions' })).toBeInTheDocument();
  });
});
