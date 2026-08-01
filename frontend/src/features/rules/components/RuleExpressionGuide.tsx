import { useQuery } from '@tanstack/react-query';
import { BookOpenText } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  keyboardFocusRing,
  persistentItemHighlight,
  searchMatchHighlight,
} from '@/components/shared/interactionStates';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from '@/components/ui/sheet';
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import { cn } from '@/lib/utils';
import type {
  RuleExpressionGuide as RuleExpressionGuideDocument,
  RuleExpressionReferenceKind,
  RuleInputDefinition,
} from '../api';
import {
  ruleDefinitionQueryKeys,
  type SearchRuleExpressionGuideRequest,
  searchRuleExpressionGuide,
} from '../api';

export interface RuleExpressionGuideTarget {
  referenceKind: RuleExpressionReferenceKind;
  referenceKey: string;
  displayText?: string;
}

export function RuleExpressionGuide({
  expressionLanguageVersion = 1,
  definitionKey,
  inputs = [],
  open,
  onOpenChange,
  target,
  trigger = true,
}: {
  expressionLanguageVersion?: number;
  definitionKey?: string;
  inputs?: RuleInputDefinition[];
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  target?: RuleExpressionGuideTarget | null;
  trigger?: boolean;
}) {
  const { t, i18n } = useTranslation();
  const [internalOpen, setInternalOpen] = useState(false);
  const resolvedOpen = open ?? internalOpen;
  const setOpen = (next: boolean) => {
    if (open === undefined) setInternalOpen(next);
    onOpenChange?.(next);
  };

  return (
    <Sheet open={resolvedOpen} onOpenChange={setOpen}>
      {trigger ? (
        <SheetTrigger render={<Button type="button" variant="outline" size="sm" />}>
          <BookOpenText data-icon="inline-start" aria-hidden />
          {t('rules.expressionGuideAction')}
        </SheetTrigger>
      ) : null}
      <SheetContent className="gap-0 overflow-hidden sm:max-w-2xl">
        <SheetHeader className="border-b">
          <SheetTitle>{t('rules.expressionGuideTitle')}</SheetTitle>
          <SheetDescription>{t('rules.expressionGuideDescription')}</SheetDescription>
        </SheetHeader>
        <ExpressionGuideContent
          open={resolvedOpen}
          request={{
            expressionLanguageVersion,
            definitionKey: definitionKey ?? null,
            inputs,
            query: null,
            language: i18n.language,
          }}
          target={target}
        />
      </SheetContent>
    </Sheet>
  );
}

function ExpressionGuideContent({
  open,
  request,
  target,
}: {
  open: boolean;
  request: SearchRuleExpressionGuideRequest;
  target?: RuleExpressionGuideTarget | null;
}) {
  const { t } = useTranslation();
  const [query, setQuery] = useState('');
  const debouncedQuery = useDebouncedValue(query);
  const searchRequest = useMemo(
    () => ({ ...request, query: debouncedQuery.trim() || null }),
    [debouncedQuery, request],
  );
  const guideQuery = useQuery({
    queryKey: ruleDefinitionQueryKeys.expressionGuide(searchRequest),
    queryFn: ({ signal }) => searchRuleExpressionGuide(searchRequest, signal),
    enabled: open,
    staleTime: 1000 * 60 * 5,
  });
  const document = guideQuery.data;
  const targetId = target ? guideItemId(target.referenceKind, target.referenceKey) : undefined;
  const targetAvailable = hasTarget(document, targetId);
  const hasSearch = query.trim().length > 0;

  useEffect(() => {
    if (!open || !targetId) return;
    setQuery('');
  }, [open, targetId]);

  useEffect(() => {
    if (!open || !targetId || !targetAvailable) return;
    requestAnimationFrame(() => {
      const item = globalThis.document.getElementById(targetId);
      item?.scrollIntoView?.({ block: 'center' });
      item?.focus({ preventScroll: true });
    });
  }, [open, targetAvailable, targetId]);

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="shrink-0 space-y-3 border-b px-4 py-3">
        <Input
          type="search"
          value={query}
          aria-label={t('rules.expressionGuideSearchLabel')}
          placeholder={t('rules.expressionGuideSearchPlaceholder')}
          onChange={(event) => setQuery(event.target.value)}
        />
        {!guideQuery.isError && document ? (
          <p role="status" className="text-xs font-medium text-muted-foreground">
            {hasSearch
              ? t('rules.expressionGuideSearchResultCount', {
                  count: document.totalResults ?? 0,
                  query: query.trim(),
                })
              : t('rules.expressionGuideResultCount', {
                  count: document.totalResults ?? 0,
                })}
          </p>
        ) : null}
        {!hasSearch && document?.sections?.length ? (
          <nav
            aria-label={t('rules.expressionGuideSectionsLabel')}
            className="flex flex-wrap gap-x-3 gap-y-1"
          >
            {document.sections.map((section) => (
              <Button
                key={section.key}
                type="button"
                variant="link"
                size="xs"
                className="h-auto p-0"
                onClick={() =>
                  globalThis.document
                    .getElementById(guideSectionId(section.key ?? ''))
                    ?.scrollIntoView?.({ block: 'start' })
                }
              >
                {section.title}
              </Button>
            ))}
          </nav>
        ) : null}
      </div>

      <div className="min-h-0 flex-1 space-y-7 overflow-y-auto px-4 py-5">
        {guideQuery.isLoading || guideQuery.isFetching ? (
          <p role="status" className="text-sm text-muted-foreground">
            {t('rules.referenceLoading')}
          </p>
        ) : null}

        {guideQuery.isError ? (
          <div role="alert" className="space-y-2 text-sm">
            <p className="text-destructive">{t('rules.loadErrorTitle')}</p>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => void guideQuery.refetch()}
            >
              {t('app.retry')}
            </Button>
          </div>
        ) : null}

        {!guideQuery.isFetching && !guideQuery.isError && (document?.totalResults ?? 0) === 0 ? (
          <p role="status" className="text-sm text-muted-foreground">
            {t('rules.expressionGuideSearchEmpty')}
          </p>
        ) : null}

        {(document?.sections ?? []).map((section) => (
          <ReferenceSection
            key={section.key}
            section={section}
            target={hasSearch ? null : target}
          />
        ))}
      </div>
    </div>
  );
}

function ReferenceSection({
  section,
  target,
}: {
  section: NonNullable<RuleExpressionGuideDocument['sections']>[number];
  target?: RuleExpressionGuideTarget | null;
}) {
  const { t } = useTranslation();
  const targetId = target ? guideItemId(target.referenceKind, target.referenceKey) : undefined;
  return (
    <section id={guideSectionId(section.key ?? '')} className="scroll-mt-4 space-y-3">
      <div>
        <h3 className="font-heading text-base font-medium text-foreground">{section.title}</h3>
        <p className="mt-1 text-sm/relaxed text-muted-foreground">{section.description}</p>
      </div>
      <ul className="divide-y divide-border">
        {(section.items ?? []).map((item) => {
          const itemId = guideItemId(item.referenceKind, item.referenceKey ?? '');
          const isTarget = itemId === targetId;
          return (
            <li
              id={itemId}
              key={`${item.referenceKind}:${item.referenceKey}`}
              tabIndex={-1}
              aria-current={isTarget ? 'true' : undefined}
              className={cn(
                'scroll-mt-4 rounded-md px-3 py-4 outline-none',
                keyboardFocusRing,
                isTarget && persistentItemHighlight,
              )}
            >
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <h4 className="text-sm font-semibold text-foreground">
                    {isTarget && target?.displayText ? (
                      target.displayText
                    ) : (
                      <SearchText value={item.displayName} />
                    )}
                  </h4>
                  <p className="mt-1 text-sm/relaxed text-muted-foreground">
                    <SearchText value={item.summary} />
                  </p>
                </div>
              </div>
              <dl className="mt-4 grid gap-3">
                <div>
                  <dt className="text-xs font-medium text-muted-foreground">
                    {t('rules.expressionGuideUsageLabel')}
                  </dt>
                  <dd className="mt-1 text-sm/relaxed">
                    <SearchText value={item.usage} />
                  </dd>
                </div>
                {hasMatchedSegment(item.detail) ? (
                  <div>
                    <dt className="text-xs font-medium text-muted-foreground">
                      {t('rules.expressionGuideReferenceLabel')}
                    </dt>
                    <dd className="mt-1 rounded-sm bg-muted px-2 py-1.5 font-mono text-xs">
                      <SearchText value={item.detail} />
                    </dd>
                  </div>
                ) : null}
                {!isTarget && (item.examples ?? []).length > 0 ? (
                  <div>
                    <dt className="text-xs font-medium text-muted-foreground">
                      {t('rules.expressionGuideExamplesLabel')}
                    </dt>
                    <dd className="mt-1 space-y-1">
                      {(item.examples ?? []).map((example) => (
                        <code
                          key={example.text}
                          className="block rounded-sm bg-muted px-2 py-1.5 font-mono text-xs"
                        >
                          <SearchText value={example} />
                        </code>
                      ))}
                    </dd>
                  </div>
                ) : null}
              </dl>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

function SearchText({
  value,
}: {
  value?: NonNullable<
    NonNullable<RuleExpressionGuideDocument['sections']>[number]['items']
  >[number]['displayName'];
}) {
  if (!value) return null;
  return (
    <>
      {(value.segments ?? []).map((segment, index) =>
        segment.isMatch ? (
          <mark
            // biome-ignore lint/suspicious/noArrayIndexKey: Ordered immutable text segments have no separate identity.
            key={index}
            className={searchMatchHighlight}
          >
            {segment.text}
          </mark>
        ) : (
          // biome-ignore lint/suspicious/noArrayIndexKey: Ordered immutable text segments have no separate identity.
          <span key={index}>{segment.text}</span>
        ),
      )}
    </>
  );
}

function hasMatchedSegment(
  value:
    | NonNullable<
        NonNullable<RuleExpressionGuideDocument['sections']>[number]['items']
      >[number]['detail']
    | undefined,
) {
  return value?.segments?.some((segment) => segment.isMatch) === true;
}

function hasTarget(
  document: RuleExpressionGuideDocument | undefined,
  targetId: string | undefined,
) {
  if (!document || !targetId) return false;
  return (document.sections ?? []).some((section) =>
    (section.items ?? []).some(
      (item) => guideItemId(item.referenceKind, item.referenceKey ?? '') === targetId,
    ),
  );
}

function guideSectionId(key: string) {
  return `rule-expression-guide-section-${encodeURIComponent(key)}`;
}

function guideItemId(kind: string | null | undefined, key: string) {
  return `rule-expression-guide-item-${encodeURIComponent(kind ?? 'unknown')}-${encodeURIComponent(key)}`;
}
