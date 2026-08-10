import { render, screen } from '@testing-library/react';
import { describe, expect, expectTypeOf, it } from 'vitest';
import { PageAction } from '../src/components/shared/PageLayout';
import {
  ResourceWorkspace,
  type ResourceWorkspaceProps,
} from '../src/components/shared/ResourceWorkspace';

describe('ResourceWorkspace', () => {
  it('owns the contained resource-page anatomy and named slots', () => {
    const { container } = render(
      <ResourceWorkspace
        title="Business objects"
        description="Manage definitions."
        actions={<PageAction>Create</PageAction>}
        status={<p role="status">Authorization unavailable</p>}
      >
        <section aria-label="Definitions">Table</section>
      </ResourceWorkspace>,
    );

    const layout = container.querySelector('[data-slot="page-layout"]');
    const workspace = container.querySelector('[data-slot="resource-workspace"]');
    const content = container.querySelector('[data-slot="resource-workspace-content"]');

    expect(layout).toHaveAttribute('data-scroll-mode', 'contained');
    expect(layout).toContainElement(workspace);
    expect(workspace).toContainElement(screen.getByRole('heading', { name: 'Business objects' }));
    expect(workspace).toContainElement(screen.getByRole('status'));
    expect(content).toContainElement(screen.getByRole('region', { name: 'Definitions' }));
  });

  it('does not expose layout or scroll escape hatches', () => {
    expectTypeOf<ResourceWorkspaceProps>().not.toHaveProperty('className');
    expectTypeOf<ResourceWorkspaceProps>().not.toHaveProperty('scrollMode');
  });
});
