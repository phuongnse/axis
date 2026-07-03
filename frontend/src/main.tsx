import { QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { createRouter, RouterProvider } from '@tanstack/react-router';
import React from 'react';
import ReactDOM from 'react-dom/client';
import './index.css';
import './features/preferences/i18n';

import { useTranslation } from 'react-i18next';
import { shouldRenderDevtools } from './lib/devtools';
import { queryClient } from './lib/query-client';
import { routeTree } from './routeTree.gen';

function NotFound() {
  const { t } = useTranslation();
  return <p className="p-8">{t('app.pageNotFound')}</p>;
}

const router = createRouter({
  routeTree,
  context: {
    queryClient,
  },
  defaultNotFoundComponent: NotFound,
  defaultPreload: 'intent',
  // Preloads should run loaders, not reuse stale route matches.
  defaultPreloadStaleTime: 0,
});

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}

const rootElement = document.getElementById('root');
if (!rootElement) {
  throw new Error('Root element not found');
}
if (!rootElement.innerHTML) {
  const root = ReactDOM.createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
        {shouldRenderDevtools() ? <ReactQueryDevtools initialIsOpen={false} /> : null}
      </QueryClientProvider>
    </React.StrictMode>,
  );
}
