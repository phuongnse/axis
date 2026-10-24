import { createElement, type ReactNode } from 'react'
import { createRoot } from 'react-dom/client'
import { installFixtureFetch } from './fixtureFetch'
import { fixtureScopes, routes } from './fixtures'
import { Preview } from './Preview'
import { previews } from './previews'

installFixtureFetch(routes, fixtureScopes)

/** Mounts a preview into an element of the page and returns the function that unmounts it. */
function mount(draw: () => ReactNode): (element: HTMLElement) => () => void {
  return (element) => {
    const root = createRoot(element)
    root.render(createElement(Preview, { draw }))
    return () => root.unmount()
  }
}

/** `window.Axis`: one mount per preview. */
export default Object.fromEntries(Object.entries(previews).map(([name, draw]) => [name, mount(draw)]))
