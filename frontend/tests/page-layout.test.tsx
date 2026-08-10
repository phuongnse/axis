import { render, screen } from '@testing-library/react';
import { describe, expect, expectTypeOf, it } from 'vitest';
import {
  EntryLayout,
  type EntryLayoutProps,
  PageAction,
  type PageActionProps,
  PageHeader,
  type PageHeaderProps,
  PageLayout,
  type PageLayoutProps,
  SectionHeader,
  type SectionHeaderProps,
} from '../src/components/shared/PageLayout';

describe('EntryLayout', () => {
  it('owns the full-viewport entry anatomy and optional utilities without horizontal overflow', () => {
    const { container } = render(
      <EntryLayout utilities={<button type="button">Preferences</button>}>
        <section>Entry content</section>
      </EntryLayout>,
    );

    const layout = container.querySelector<HTMLElement>('[data-slot="entry-layout"]');
    const utilities = container.querySelector<HTMLElement>('[data-slot="entry-utilities"]');
    const content = screen.getByRole('main');

    expect(layout).toHaveClass(
      'flex',
      'min-h-dvh',
      'w-full',
      'min-w-0',
      'flex-col',
      'overflow-x-hidden',
      'bg-background',
      'p-axis-page-compact',
      'sm:p-axis-page-default',
      'lg:p-axis-page-wide',
    );
    expect(utilities).toContainElement(screen.getByRole('button', { name: 'Preferences' }));
    expect(content).toHaveAttribute('data-slot', 'entry-content');
    expect(content).toHaveClass(
      'mx-auto',
      'flex',
      'w-full',
      'max-w-lg',
      'flex-1',
      'items-center',
      'justify-center',
      'py-axis-region',
    );
    expect(content).toHaveTextContent('Entry content');
  });

  it('omits utilities cleanly and does not expose a free-form className escape hatch', () => {
    const { container } = render(<EntryLayout>Entry content</EntryLayout>);

    expect(container.querySelector('[data-slot="entry-utilities"]')).toBeNull();
    expectTypeOf<EntryLayoutProps>().not.toHaveProperty('className');
  });
});

describe('PageLayout', () => {
  it.each([
    ['contained', ['overflow-hidden'], ['overflow-x-hidden', 'overflow-y-auto']],
    ['route', ['overflow-x-hidden', 'overflow-y-auto'], ['overflow-hidden']],
  ] as const)('owns the %s scroll mode inside the fixed route work area', (scrollMode, overflowClasses, excludedOverflowClasses) => {
    const { container } = render(<PageLayout scrollMode={scrollMode}>Page content</PageLayout>);

    const page = container.querySelector<HTMLElement>('[data-slot="page-layout"]');
    expect(page?.tagName).toBe('DIV');
    expect(page).toHaveAttribute('data-scroll-mode', scrollMode);
    expect(page).toHaveClass(
      'flex',
      'h-full',
      'min-h-0',
      'w-full',
      'min-w-0',
      'flex-col',
      'gap-axis-region',
      'p-axis-page-compact',
      'sm:p-axis-page-default',
      'lg:p-axis-page-wide',
      ...overflowClasses,
    );
    expect(page).not.toHaveClass(...excludedOverflowClasses);
  });

  it('does not expose a free-form className escape hatch', () => {
    expectTypeOf<PageLayoutProps>().not.toHaveProperty('className');
  });
});

describe('PageHeader', () => {
  it('provides the page landmark, heading hierarchy, description, and responsive actions', () => {
    const { container } = render(
      <PageHeader
        title="Business objects"
        description="Define reusable business concepts."
        actions={<PageAction type="button">Create</PageAction>}
      />,
    );

    const header = container.querySelector<HTMLElement>('[data-slot="page-header"]');
    const heading = screen.getByRole('heading', { level: 1, name: 'Business objects' });
    const description = screen.getByText('Define reusable business concepts.');
    const actions = container.querySelector<HTMLElement>('[data-slot="page-actions"]');

    expect(header?.tagName).toBe('HEADER');
    expect(header).toHaveClass(
      'flex',
      'min-w-0',
      'shrink-0',
      'flex-col',
      'gap-axis-region',
      'sm:flex-row',
      'sm:items-start',
      'sm:justify-between',
    );
    expect(heading).toHaveAttribute('data-slot', 'page-title');
    expect(heading).toHaveClass(
      'font-heading',
      'text-axis-page-title',
      'font-axis-page-title',
      'text-foreground',
    );
    expect(description).toHaveAttribute('data-slot', 'page-description');
    expect(description).toHaveClass(
      'max-w-3xl',
      'text-axis-body',
      'font-axis-body',
      'text-muted-foreground',
    );
    expect(actions).toHaveClass(
      'flex',
      'w-full',
      'flex-wrap',
      'items-center',
      'gap-axis-inline',
      'sm:w-auto',
      'sm:justify-end',
    );
    expect(screen.getByRole('button', { name: 'Create' })).toHaveClass(
      'min-h-axis-touch-target',
      'min-w-axis-touch-target',
      'sm:min-h-axis-compact-control',
      'sm:min-w-axis-compact-control',
    );
  });

  it('omits optional description and action slots cleanly', () => {
    const { container } = render(<PageHeader title="Settings" />);

    expect(screen.getByRole('heading', { level: 1, name: 'Settings' })).toBeInTheDocument();
    expect(container.querySelector('[data-slot="page-description"]')).toBeNull();
    expect(container.querySelector('[data-slot="page-actions"]')).toBeNull();
  });

  it('supports conditional authorized actions without widening the action contract', () => {
    const canCreate = false;
    render(
      <PageHeader
        title="Business objects"
        actions={[
          canCreate && <PageAction key="create">Create</PageAction>,
          null,
          <PageAction key="refresh" variant="outline">
            Refresh
          </PageAction>,
        ]}
      />,
    );

    expect(screen.queryByRole('button', { name: 'Create' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Refresh' })).toBeInTheDocument();
  });

  it('does not expose a free-form className escape hatch', () => {
    expectTypeOf<PageHeaderProps>().not.toHaveProperty('className');
  });
});

describe('PageAction', () => {
  it('forwards shared Button props and render behavior with owned touch sizing', () => {
    render(
      <PageAction
        nativeButton={false}
        render={<a href="/business-objects/new">Define object</a>}
        variant="outline"
      />,
    );

    const action = screen.getByRole('button', { name: 'Define object' });
    expect(action.tagName).toBe('A');
    expect(action).toHaveAttribute('data-slot', 'button');
    expect(action).toHaveAttribute('href', '/business-objects/new');
    expect(action).toHaveClass(
      'h-8',
      'min-h-axis-touch-target',
      'min-w-axis-touch-target',
      'sm:min-h-axis-compact-control',
      'sm:min-w-axis-compact-control',
      'border-border',
    );
  });

  it('does not expose a free-form className escape hatch', () => {
    expectTypeOf<PageActionProps>().not.toHaveProperty('className');
  });
});

describe('SectionHeader', () => {
  it('owns section typography, description, and contextual actions', () => {
    const { container } = render(
      <SectionHeader
        id="release-title"
        title="Release"
        description="Inspect immutable release facts."
        actions={<span>Trusted</span>}
      />,
    );

    const header = container.querySelector('[data-slot="section-header"]');
    const title = screen.getByRole('heading', { level: 2, name: 'Release' });
    const description = screen.getByText('Inspect immutable release facts.');

    expect(header).toHaveClass(
      'flex',
      'min-w-0',
      'flex-wrap',
      'items-start',
      'justify-between',
      'gap-axis-region',
    );
    expect(title).toHaveAttribute('id', 'release-title');
    expect(title).toHaveClass('font-heading', 'text-axis-section-title', 'font-axis-section-title');
    expect(description).toHaveAttribute('data-slot', 'section-description');
    expect(container.querySelector('[data-slot="section-actions"]')).toHaveTextContent('Trusted');
  });

  it('does not expose a free-form className escape hatch', () => {
    expectTypeOf<SectionHeaderProps>().not.toHaveProperty('className');
  });
});
