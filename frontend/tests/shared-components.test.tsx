import { render, screen } from '@testing-library/react';
import { Search } from 'lucide-react';
import { describe, expect, it } from 'vitest';

import { ActionLink } from '../src/components/shared/ActionLink';
import { ContentGrid } from '../src/components/shared/ContentGrid';
import { PageHeader } from '../src/components/shared/PageHeader';
import { Toolbar } from '../src/components/shared/Toolbar';

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

describe('ContentGrid', () => {
  it('renders grouped content without changing child semantics', () => {
    render(
      <ContentGrid>
        <section aria-label="Account">Account</section>
        <section aria-label="Profile">Profile</section>
      </ContentGrid>,
    );

    expect(screen.getByRole('region', { name: 'Account' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Profile' })).toBeInTheDocument();
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
