import '@ant-design/v5-patch-for-react-19'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import { App } from './App'
import { ThemeModeProvider } from './platform/theme'

const root = document.getElementById('root')
if (!root) {
  throw new Error('Root element #root is missing.')
}

createRoot(root).render(
  <StrictMode>
    <BrowserRouter>
      <ThemeModeProvider>
        <App development={import.meta.env.DEV} />
      </ThemeModeProvider>
    </BrowserRouter>
  </StrictMode>,
)
