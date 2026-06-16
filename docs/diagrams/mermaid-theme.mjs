/**
 * Canonical Mermaid theme for all docs (README, use cases, playbooks).
 * Paste MERMAID_INIT as the first line inside every ```mermaid fence.
 *
 *   node -e "import('./docs/diagrams/mermaid-theme.mjs').then(m=>console.log(m.MERMAID_INIT))"
 */

/** Dark canvas + blue borders — matches GitHub dark-mode Mermaid (see register-workspace Screen flow). */
export const MERMAID_INIT =
  "%%{init: {'theme':'dark','themeVariables':{'background':'#0d1117','mainBkg':'#0d1117','primaryColor':'#161b22','primaryBorderColor':'#388bfd','primaryTextColor':'#e6edf3','secondaryColor':'#21262d','secondaryBorderColor':'#388bfd','secondaryTextColor':'#e6edf3','tertiaryColor':'#161b22','tertiaryTextColor':'#e6edf3','lineColor':'#58a6ff','textColor':'#e6edf3','nodeBorder':'#388bfd','clusterBkg':'#161b22','clusterBorder':'#388bfd','titleColor':'#e6edf3','edgeLabelBackground':'#161b22','actorBkg':'#161b22','actorBorder':'#388bfd','actorTextColor':'#e6edf3','signalColor':'#58a6ff','labelBoxBkgColor':'#161b22','labelBoxBorderColor':'#388bfd','noteBkgColor':'#161b22','noteBorderColor':'#388bfd','noteTextColor':'#c9d1d9','activationBkgColor':'#30363d'}}}%%";

/** Sequence-diagram phase bands (rect rgb(...)). */
export const SEQUENCE_PHASE_RGB = "22, 35, 58";
