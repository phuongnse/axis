import { afterEach, beforeEach } from 'vitest'

// jsdom matches every style rule against each element it checks, which made the Vitest suite slow.
let styles = false
const parking = document.createElement('div')

function isStyle(node: Node): node is HTMLStyleElement {
  return node instanceof HTMLStyleElement
}

/** Keeps style elements added to the head out of the document while styles are off. */
export function installStyleSwitch(): void {
  const head = document.head
  const appendNode = Node.prototype.appendChild
  const insertNode = Node.prototype.insertBefore
  const removeNode = Node.prototype.removeChild

  head.appendChild = <T extends Node>(node: T): T => {
    if (styles || !isStyle(node)) return appendNode.call(head, node) as T
    parking.appendChild(node)
    return node
  }
  head.insertBefore = <T extends Node>(node: T, child: Node | null): T => {
    if (styles || !isStyle(node)) return insertNode.call(head, node, child) as T
    parking.insertBefore(node, child?.parentNode === parking ? child : null)
    return node
  }
  head.removeChild = <T extends Node>(child: T): T => {
    if (child.parentNode !== parking) return removeNode.call(head, child) as T
    parking.removeChild(child)
    return child
  }
}

function setStyles(on: boolean): void {
  styles = on
  if (on) {
    for (const node of Array.from(parking.childNodes)) document.head.appendChild(node)
  } else {
    for (const style of Array.from(document.head.querySelectorAll('style'))) parking.appendChild(style)
  }
}

/**
 * Gives the tests in this scope Ant Design's styles. Call it at file scope or in a `describe`. A single
 * test goes in its own `describe`, so the other tests of the file keep rendering without styles.
 */
export function withAntdStyles(): void {
  beforeEach(() => setStyles(true))
  afterEach(() => setStyles(false))
}
