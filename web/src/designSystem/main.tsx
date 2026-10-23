import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { DesignSystemPage } from './DesignSystemPage'
import { installFixtureFetch } from './fixtureFetch'
import { routes } from './fixtures'

const root = document.getElementById('root')
if (!root) {
  throw new Error('Root element #root is missing.')
}

installFixtureFetch(routes)

createRoot(root).render(
  <StrictMode>
    <DesignSystemPage search={window.location.search} />
  </StrictMode>,
)
