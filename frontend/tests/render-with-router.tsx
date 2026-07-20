import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  RouterProvider,
} from '@tanstack/react-router';
import { act, type RenderOptions, type RenderResult, render } from '@testing-library/react';
import type { ReactElement } from 'react';
import { ManagedWindowHost } from '@/components/shared/ManagedWindowHost';
import { ManagedWindowProvider } from '@/components/shared/ManagedWindowManager';
import { managedWindowRenderers } from '@/lib/managed-window-registry';

interface RenderWithRouterOptions extends Omit<RenderOptions, 'wrapper'> {
  path?: string;
  authenticatedPath?: string;
}

interface RenderWithRouterResult extends RenderResult {
  router: ReturnType<typeof createRouter>;
}

export async function renderWithRouter(
  ui: ReactElement,
  options: RenderWithRouterOptions = {},
): Promise<RenderWithRouterResult> {
  const { path = '/', authenticatedPath, ...renderOptions } = options;
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  const rootRoute = createRootRoute({ component: authenticatedPath ? Outlet : () => ui });
  let routeTree = rootRoute;
  if (authenticatedPath) {
    const authenticatedRoute = createRoute({
      getParentRoute: () => rootRoute,
      id: '_authenticated',
      component: Outlet,
    });
    const pageRoute = createRoute({
      getParentRoute: () => authenticatedRoute,
      path: authenticatedPath,
      validateSearch: (search: Record<string, unknown>) => search,
      component: () => ui,
    });
    routeTree = rootRoute.addChildren([authenticatedRoute.addChildren([pageRoute])]);
  }

  const router = createRouter({
    routeTree,
    context: { queryClient },
    history: createMemoryHistory({ initialEntries: [path] }),
  });

  await act(async () => {
    await router.load();
  });

  const result = render(
    <QueryClientProvider client={queryClient}>
      <ManagedWindowProvider renderers={managedWindowRenderers}>
        <div className="relative h-dvh w-dvw">
          <RouterProvider router={router} />
          <ManagedWindowHost />
        </div>
      </ManagedWindowProvider>
    </QueryClientProvider>,
    renderOptions,
  );

  await act(async () => {});

  return { router, ...result };
}
