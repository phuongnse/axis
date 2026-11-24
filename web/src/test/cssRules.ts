/**
 * The value that wins in the cascade for a property of an element: the last rule that matches and sets it. jsdom's
 * computed style drops the rules of a style element, so the test reads the rules, as `selectedMatchBackground` does.
 * With `hovered`, a `:hover` selector counts as matching the element it is on, because jsdom cannot hover, so the
 * test reads the rules a hovered element would match.
 */
export function winningRule(element: Element, property: string, { hovered = false }: { hovered?: boolean } = {}) {
  const rules = Array.from(document.styleSheets).flatMap((sheet) => Array.from(sheet.cssRules))
  const styleRules = rules.filter((rule): rule is CSSStyleRule => 'selectorText' in rule)
  return styleRules
    .filter((rule) => element.matches(hovered ? rule.selectorText.replaceAll(':hover', '') : rule.selectorText))
    .map((rule) => rule.style.getPropertyValue(property))
    .filter(Boolean)
    .at(-1)
}
