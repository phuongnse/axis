import {
  ArrowRight,
  Check,
  CheckCircle2,
  ExternalLink,
  Eye,
  LoaderCircle,
  MoreHorizontal,
  Palette,
  Save,
  Search,
  Settings2,
  ShieldAlert,
  Trash2,
} from 'lucide-react';
import type { ReactNode } from 'react';

import { ActionLink } from '@/components/ui/action-link';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { CheckboxField } from '@/components/ui/checkbox-field';
import { FormField } from '@/components/ui/form-field';
import { IconButton } from '@/components/ui/icon-button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select } from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';

interface Swatch {
  label: string;
  className: string;
  textClassName?: string;
}

const semanticSwatches: Swatch[] = [
  { label: 'Background', className: 'bg-background', textClassName: 'text-foreground' },
  { label: 'Foreground', className: 'bg-foreground', textClassName: 'text-background' },
  { label: 'Card', className: 'bg-card', textClassName: 'text-card-foreground' },
  { label: 'Primary', className: 'bg-primary', textClassName: 'text-primary-foreground' },
  { label: 'Secondary', className: 'bg-secondary', textClassName: 'text-secondary-foreground' },
  { label: 'Muted', className: 'bg-muted', textClassName: 'text-muted-foreground' },
  { label: 'Accent', className: 'bg-accent', textClassName: 'text-accent-foreground' },
  {
    label: 'Destructive',
    className: 'bg-destructive',
    textClassName: 'text-destructive-foreground',
  },
];

const chartSwatches: Swatch[] = [
  { label: 'Chart 1', className: 'bg-chart-1', textClassName: 'text-white' },
  { label: 'Chart 2', className: 'bg-chart-2', textClassName: 'text-white' },
  { label: 'Chart 3', className: 'bg-chart-3', textClassName: 'text-white' },
  { label: 'Chart 4', className: 'bg-chart-4', textClassName: 'text-white' },
  { label: 'Chart 5', className: 'bg-chart-5', textClassName: 'text-white' },
];

function CatalogSection({
  title,
  eyebrow,
  children,
}: {
  title: string;
  eyebrow: string;
  children: ReactNode;
}) {
  return (
    <section className="border-t border-border py-8" data-visual-target={eyebrow}>
      <div className="grid gap-6 lg:grid-cols-[16rem_minmax(0,1fr)]">
        <div>
          <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">
            {eyebrow}
          </p>
          <h2 className="mt-2 text-xl font-semibold text-foreground">{title}</h2>
        </div>
        <div>{children}</div>
      </div>
    </section>
  );
}

function SwatchGrid({ swatches }: { swatches: Swatch[] }) {
  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
      {swatches.map((swatch) => (
        <div
          key={swatch.label}
          className="overflow-hidden rounded-lg border border-border bg-card shadow-sm"
        >
          <div className={`flex h-24 items-end p-3 ${swatch.className} ${swatch.textClassName}`}>
            <span className="text-sm font-medium">{swatch.label}</span>
          </div>
          <div className="border-t border-border px-3 py-2 text-xs text-muted-foreground">
            {swatch.className.replace('bg-', '')}
          </div>
        </div>
      ))}
    </div>
  );
}

function ButtonMatrix() {
  return (
    <div className="space-y-6">
      <div className="flex flex-wrap gap-3">
        <Button type="button" variant="cta">
          <Save className="size-4" aria-hidden />
          Save changes
        </Button>
        <Button type="button">
          <ArrowRight className="size-4" aria-hidden />
          Continue
        </Button>
        <Button type="button" variant="outline">
          <Settings2 className="size-4" aria-hidden />
          Configure
        </Button>
        <Button type="button" variant="secondary">
          <CheckCircle2 className="size-4" aria-hidden />
          Confirm
        </Button>
        <Button type="button" variant="destructive">
          <Trash2 className="size-4" aria-hidden />
          Remove
        </Button>
        <Button type="button" variant="cta" isLoading loadingLabel="Saving">
          <Save className="size-4" aria-hidden />
          Saving
        </Button>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <Button type="button" size="xs" variant="outline">
          <Check className="size-3" aria-hidden />
          Extra small
        </Button>
        <Button type="button" size="sm" variant="outline">
          <Check className="size-4" aria-hidden />
          Small
        </Button>
        <Button type="button" size="lg" variant="outline">
          <Check className="size-4" aria-hidden />
          Large
        </Button>
        <Button type="button" disabled>
          <LoaderCircle className="size-4" aria-hidden />
          Disabled
        </Button>
      </div>
    </div>
  );
}

function IconButtonMatrix() {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <IconButton type="button" icon={Search} label="Search catalog" />
      <IconButton type="button" icon={Settings2} label="Configure catalog" variant="secondary" />
      <IconButton type="button" icon={MoreHorizontal} label="More catalog actions" size="icon-sm" />
      <IconButton
        type="button"
        icon={Trash2}
        label="Remove item"
        variant="destructive"
        size="icon-lg"
      />
      <IconButton
        type="button"
        icon={Eye}
        label="Preview catalog"
        isLoading
        loadingLabel="Loading preview"
      />
      <IconButton type="button" icon={ShieldAlert} label="Disabled attention state" disabled />
    </div>
  );
}

function ActionLinkMatrix() {
  return (
    <div className="grid gap-4 lg:grid-cols-3">
      <div className="space-y-3 rounded-lg border border-border bg-card p-4">
        <p className="text-sm font-medium text-foreground">Default surface</p>
        <ActionLink to="/" icon={ArrowRight}>
          Open catalog
        </ActionLink>
        <ActionLink to="/" icon={ExternalLink} variant="secondary">
          Secondary link
        </ActionLink>
      </div>
      <div className="space-y-3 rounded-lg border border-border bg-[hsl(var(--action-inverse-foreground))] p-4">
        <p className="text-sm font-medium text-white">Inverted surface</p>
        <ActionLink to="/" icon={ArrowRight} surface="inverted">
          Start flow
        </ActionLink>
        <ActionLink to="/" icon={ExternalLink} surface="inverted" variant="secondary">
          Learn more
        </ActionLink>
      </div>
      <div className="space-y-3 rounded-lg border border-border bg-card p-4 dark:bg-[hsl(var(--action-inverse-foreground))]">
        <p className="text-sm font-medium text-foreground dark:text-white">Adaptive surface</p>
        <ActionLink to="/" icon={ArrowRight} surface="adaptive">
          Primary action
        </ActionLink>
        <ActionLink to="/" icon={ExternalLink} surface="adaptive" variant="secondary">
          Secondary action
        </ActionLink>
      </div>
    </div>
  );
}

function FormMatrix() {
  return (
    <div className="grid gap-5 lg:grid-cols-2">
      <div className="space-y-4 rounded-lg border border-border bg-card p-4">
        <FormField id="catalog-name" label="Name" helpText="Use the short component name.">
          {({ describedBy }) => (
            <Input
              id="catalog-name"
              defaultValue="Workflow designer"
              aria-describedby={describedBy}
            />
          )}
        </FormField>
        <FormField id="catalog-search" label="Search">
          {({ describedBy }) => (
            <div className="relative">
              <Search
                className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
                aria-hidden
              />
              <Input
                id="catalog-search"
                className="pl-9"
                defaultValue="Button"
                aria-describedby={describedBy}
              />
            </div>
          )}
        </FormField>
        <FormField id="catalog-state" label="State">
          {({ describedBy }) => (
            <Select id="catalog-state" defaultValue="ready" aria-describedby={describedBy}>
              <option value="ready">Ready</option>
              <option value="loading">Loading</option>
              <option value="error">Error</option>
            </Select>
          )}
        </FormField>
      </div>
      <div className="space-y-4 rounded-lg border border-border bg-card p-4">
        <FormField id="catalog-invalid" label="Invalid field" error="Use a complete email address.">
          {({ describedBy }) => (
            <Input
              id="catalog-invalid"
              aria-invalid
              defaultValue="missing-domain"
              aria-describedby={describedBy}
            />
          )}
        </FormField>
        <FormField id="catalog-notes" label="Notes" helpText="Long copy wraps inside the field.">
          {({ describedBy }) => (
            <Textarea
              id="catalog-notes"
              defaultValue="A reusable primitive should preserve height, focus, and readable line length across dense layouts."
              aria-describedby={describedBy}
            />
          )}
        </FormField>
        <CheckboxField id="catalog-terms" defaultChecked>
          Accept required terms
        </CheckboxField>
        <div className="flex items-center gap-2">
          <Checkbox id="catalog-disabled" disabled />
          <Label htmlFor="catalog-disabled" className="font-normal">
            Disabled option
          </Label>
        </div>
      </div>
    </div>
  );
}

function ThemePreview() {
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <div className="rounded-lg border border-border bg-background p-5 text-foreground">
        <p className="text-sm font-medium">Light theme target</p>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">
          Shared primitives should stay legible across dense panels, long labels, and neutral
          surfaces.
        </p>
        <div className="mt-4 flex flex-wrap gap-2">
          <span className="rounded-md border border-primary/20 bg-primary/10 px-2 py-1 text-xs font-medium text-primary">
            Success
          </span>
          <span className="rounded-md border border-destructive/20 bg-destructive/10 px-2 py-1 text-xs font-medium text-destructive">
            Warning
          </span>
        </div>
      </div>
      <div className="dark rounded-lg border border-border bg-background p-5 text-foreground">
        <p className="text-sm font-medium">Dark theme target</p>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">
          The same token names render against dark surfaces without component-specific overrides.
        </p>
        <div className="mt-4 flex flex-wrap gap-2">
          <span className="rounded-md border border-primary/20 bg-primary/10 px-2 py-1 text-xs font-medium text-primary">
            Success
          </span>
          <span className="rounded-md border border-destructive/20 bg-destructive/10 px-2 py-1 text-xs font-medium text-destructive">
            Warning
          </span>
        </div>
      </div>
    </div>
  );
}

export function DesignSystemCatalog() {
  return (
    <main className="min-h-screen bg-background text-foreground">
      <div className="mx-auto w-full max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
        <header className="pb-8" data-visual-target="catalog-header">
          <div className="flex flex-wrap items-center gap-3">
            <span className="inline-flex size-10 items-center justify-center rounded-lg border border-primary/20 bg-primary/10 text-primary">
              <Palette className="size-5" aria-hidden />
            </span>
            <div>
              <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">
                Component catalog
              </p>
              <h1 className="text-3xl font-semibold tracking-tight text-foreground">
                Axis design system
              </h1>
            </div>
          </div>
          <p className="mt-4 max-w-3xl text-sm leading-6 text-muted-foreground">
            A stable implementation target for tokens, shared primitives, states, and future visual
            QA snapshots.
          </p>
        </header>

        <CatalogSection title="Semantic color tokens" eyebrow="tokens">
          <div className="space-y-4">
            <SwatchGrid swatches={semanticSwatches} />
            <SwatchGrid swatches={chartSwatches} />
          </div>
        </CatalogSection>

        <CatalogSection title="Button" eyebrow="primitive-button">
          <ButtonMatrix />
        </CatalogSection>

        <CatalogSection title="Icon button" eyebrow="primitive-icon-button">
          <IconButtonMatrix />
        </CatalogSection>

        <CatalogSection title="Action links" eyebrow="primitive-action-link">
          <ActionLinkMatrix />
        </CatalogSection>

        <CatalogSection title="Form controls" eyebrow="primitive-form">
          <FormMatrix />
        </CatalogSection>

        <CatalogSection title="Theme coverage" eyebrow="theme-coverage">
          <ThemePreview />
        </CatalogSection>

        <CatalogSection title="State language" eyebrow="state-language">
          <div className="grid gap-4 lg:grid-cols-3">
            <div className="rounded-lg border border-border bg-card p-4">
              <CheckCircle2 className="size-5 text-primary" aria-hidden />
              <p className="mt-3 text-sm font-medium text-foreground">Ready</p>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">
                Component state uses text and icon cues alongside color.
              </p>
            </div>
            <div className="rounded-lg border border-border bg-card p-4">
              <LoaderCircle className="size-5 animate-spin text-muted-foreground" aria-hidden />
              <p className="mt-3 text-sm font-medium text-foreground">Loading</p>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">
                Async state keeps layout stable while work is pending.
              </p>
            </div>
            <div className="rounded-lg border border-destructive/30 bg-card p-4">
              <ShieldAlert className="size-5 text-destructive" aria-hidden />
              <p className="mt-3 text-sm font-medium text-foreground">Needs attention</p>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">
                Error state includes a readable message, not color alone.
              </p>
            </div>
          </div>
        </CatalogSection>
      </div>
    </main>
  );
}
