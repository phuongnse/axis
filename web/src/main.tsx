import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBrowserRouter, RouterProvider } from 'react-router'
// Only the Latin, Latin Extended and Vietnamese subsets ship (see docs/reference/frontend.md).
import './fonts.css'
import { appRoutes } from './appRoutes'
import { ThemeModeProvider } from './platform/theme'

const root = document.getElementById('root')
if (!root) {
  throw new Error('Root element #root is missing.')
}

const router = createBrowserRouter(appRoutes(import.meta.env.DEV))

createRoot(root).render(
  <StrictMode>
    <ThemeModeProvider>
      <RouterProvider router={router} />
    </ThemeModeProvider>
  </StrictMode>,
)
