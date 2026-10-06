import '@ant-design/v5-patch-for-react-19'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import { ThemeModeProvider } from './platform/theme'

const root = document.getElementById('root')
if (!root) {
  throw new Error('Root element #root is missing.')
}

createRoot(root).render(
  <StrictMode>
    <ThemeModeProvider>
      <App development={import.meta.env.DEV} />
    </ThemeModeProvider>
  </StrictMode>,
)
