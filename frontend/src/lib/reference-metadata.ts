export interface ReferenceContent {
  displayName?: string;
  summary?: string;
  usage?: string;
  examples?: string[];
}

export interface ReferenceDocumentation {
  locales?: Record<string, ReferenceContent>;
}

export function referenceContent(
  documentation: ReferenceDocumentation | null | undefined,
  language: string,
): ReferenceContent | undefined {
  const locales = documentation?.locales;
  if (!locales) return undefined;

  const normalized = language.toLowerCase();
  const base = normalized.split('-')[0];
  return (
    locales[normalized] ??
    locales[base] ??
    locales.en ??
    Object.values(locales).find((content) => content !== undefined)
  );
}

export function referenceLabel(
  documentation: ReferenceDocumentation | null | undefined,
  language: string,
  fallback = '—',
): string {
  return referenceContent(documentation, language)?.displayName || fallback;
}
