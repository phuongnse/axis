import { useDesignTokens } from './themeMode'

/**
 * Gives the attribution link of a React Flow canvas the colour tokens. React Flow's own link is a fixed grey that misses
 * the contrast of both modes. The style applies only inside the element whose class is `scope`, see
 * docs/reference/shared-widgets.md#which-widget.
 */
export function ReactFlowAttributionStyle({ scope }: { scope: string }) {
  const { colour } = useDesignTokens()
  return (
    <style>
      {`.${scope} .react-flow__attribution { background: ${colour.surface}; }
.${scope} .react-flow__attribution a { color: ${colour.textMuted}; }`}
    </style>
  )
}
