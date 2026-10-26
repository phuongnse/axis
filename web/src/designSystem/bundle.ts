import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { installFixtureFetch } from './fixtureFetch'
import { fixtureScopes, routes } from './fixtures'
import { Preview } from './Preview'
import { previews, stateRoutes, type PreviewState } from './previews'

installFixtureFetch({ ...routes, ...stateRoutes(previews) }, fixtureScopes)

/** Mounts every state of a preview into an element of the page and returns the function that unmounts them all. */
function mount(states: readonly PreviewState[]): (element: HTMLElement) => () => void {
  return (element) => {
    const root = createRoot(element)
    root.render(createElement(Preview, { states }))
    return () => root.unmount()
  }
}

/** `window.Axis`: one mount per preview, which draws every state of it. */
export default Object.fromEntries(Object.entries(previews).map(([name, states]) => [name, mount(states)]))
