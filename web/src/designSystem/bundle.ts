import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { installFixtureFetch } from './fixtureFetch'
import { installFormSubmission } from './formSubmission'
import { fixtureScopes, routes } from './fixtures'
import { Preview } from './Preview'
import { previewSessions, previews, stateRoutes, type PreviewState } from './previews'
import { setPreviewSession, shellRoutes, shellScopes } from './shellFixtures'

installFixtureFetch({ ...routes, ...shellRoutes, ...stateRoutes(previews) }, [...fixtureScopes, ...shellScopes])
installFormSubmission()

/** Mounts every state of a preview into an element of the page and returns the function that unmounts them all. */
function mount(name: string, states: readonly PreviewState[]): (element: HTMLElement) => () => void {
  return (element) => {
    setPreviewSession(previewSessions[name] ?? 'signedIn')
    const root = createRoot(element)
    root.render(createElement(Preview, { states }))
    return () => root.unmount()
  }
}

/** `window.Axis`: one mount per preview, which draws every state of it. */
export default Object.fromEntries(Object.entries(previews).map(([name, states]) => [name, mount(name, states)]))
