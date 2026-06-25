import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  createMemoryHistory,
  createRootRoute,
  createRouter,
  RouterProvider,
} from '@tanstack/react-router';
import { act, type RenderOptions, type RenderResult, render } from '@testing-library/react';
import type { ReactElement } from 'react';

interface RenderWithRouterOptions extends Omit<RenderOptions, 'wrapper'> {
  path?: string;
}

interface RenderWithRouterResult extends RenderResult {
  router: ReturnType<typeof createRouter>;
}

export async function renderWithRouter(
  ui: ReactElement,
  options: RenderWithRouterOptions = {},
): Promise<RenderWithRouterResult> {
  const { path = '/', ...renderOptions } = options;
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  const rootRoute = createRootRoute({
    component: () => ui,
  });

  const router = createRouter({
    routeTree: rootRoute,
    context: { queryClient },
    history: createMemoryHistory({ initialEntries: [path] }),
  });

  await act(async () => {
    await router.load();
  });

  const result = render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
    renderOptions,
  );

  await act(async () => {});

  return { router, ...result };
}
