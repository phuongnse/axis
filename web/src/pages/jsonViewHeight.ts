/** The least height of the view, so a tiny window still leaves room to read. */
export const minHeight = 160

export interface JsonViewHeightInput {
  windowHeight: number
  /** The top of the view on the page. */
  top: number
  footerHeight: number
  /** The space that sits below the view and above the footer: what follows the view and the content's padding. */
  below: number
  /** The gap kept between the view and the footer. */
  gap: number
}

/** The height that puts the bottom of the view `gap` above the footer, which sits at the foot of the window. */
export function jsonViewHeight({ windowHeight, top, footerHeight, below, gap }: JsonViewHeightInput): number {
  return Math.max(windowHeight - footerHeight - gap - top - below, minHeight)
}
