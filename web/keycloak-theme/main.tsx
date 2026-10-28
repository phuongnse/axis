import { createRoot } from 'react-dom/client'
// Only the Latin, Latin Extended and Vietnamese subsets ship (see docs/reference/frontend.md).
import '../src/fonts.css'
import './sign-in.css'
import { readSignInContext } from './readSignInContext'
import { SignInEntry } from './signInEntry'

const root = document.getElementById('root')
if (!root) {
  throw new Error('Root element #root is missing.')
}

createRoot(root).render(<SignInEntry context={readSignInContext(document)} />)
