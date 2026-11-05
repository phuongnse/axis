import { createContext } from 'react'
import type { KeyedLists } from '../platform/layerMarks'
import type { DesignerLayer } from './LayerMarkViews'

/** The customization layer that a schema form marks: what the layer did, and which lists are keyed. */
export interface SchemaLayer {
  designer: DesignerLayer
  keyedLists: KeyedLists
}

/** The layer of the module resource that a schema form edits, or `undefined` for a resource with no layer. */
export const SchemaLayerContext = createContext<SchemaLayer | undefined>(undefined)
