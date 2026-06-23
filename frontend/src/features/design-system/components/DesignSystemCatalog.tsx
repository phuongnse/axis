import {
  AlertTriangle,
  ArrowRight,
  Check,
  CheckCircle2,
  ExternalLink,
  Eye,
  Inbox,
  Layers3,
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
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
import { CheckboxField } from '@/components/ui/checkbox-field';
import { ContentGrid } from '@/components/ui/content-grid';
import { EmptyState } from '@/components/ui/empty-state';
import { FormField } from '@/components/ui/form-field';
import { IconButton } from '@/components/ui/icon-button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Notice } from '@/components/ui/notice';
import { PageHeader } from '@/components/ui/page-header';
import { Panel } from '@/components/ui/panel';
import { Progress } from '@/components/ui/progress';
import { Select } from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import { Textarea } from '@/components/ui/textarea';
import { Toolbar } from '@/components/ui/toolbar';
import {
  type AxisConsumerContract,
  axisConsumerContracts,
} from '@/design-system/consumer-contracts';
import {
  type AxisPrimitiveContract,
  axisPrimitiveContracts,
} from '@/design-system/primitive-contracts';

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
  { label: 'Chart 1', className: 'bg-chart-1', textClassName: 'text-chart-foreground' },
  { label: 'Chart 2', className: 'bg-chart-2', textClassName: 'text-chart-foreground' },
  { label: 'Chart 3', className: 'bg-chart-3', textClassName: 'text-chart-foreground' },
  { label: 'Chart 4', className: 'bg-chart-4', textClassName: 'text-chart-foreground' },
  { label: 'Chart 5', className: 'bg-chart-5', textClassName: 'text-chart-foreground' },
];

const stateSwatches: Swatch[] = [
  {
    label: 'Info',
    className: 'bg-state-info-background',
    textClassName: 'text-state-info-foreground',
  },
  {
    label: 'Success',
    className: 'bg-state-success-background',
    textClassName: 'text-state-success-foreground',
  },
  {
    label: 'Warning',
    className: 'bg-state-warning-background',
    textClassName: 'text-state-warning-foreground',
  },
  {
    label: 'Danger',
    className: 'bg-destructive/10',
    textClassName: 'text-destructive',
  },
];

const surfaceSwatches: Swatch[] = [
  { label: 'Inverse', className: 'bg-inverse', textClassName: 'text-inverse-foreground' },
  { label: 'Sidebar', className: 'bg-sidebar', textClassName: 'text-sidebar-foreground' },
  {
    label: 'Sidebar Accent',
    className: 'bg-sidebar-accent',
    textClassName: 'text-sidebar-foreground',
  },
  { label: 'Inverse Muted', className: 'bg-inverse-muted', textClassName: 'text-foreground' },
];

function formatContractLabel(value: string) {
  return value.replaceAll('-', ' ');
}

function ReadinessGroup({ label, values }: { label: string; values: readonly string[] }) {
  return (
    <div>
      <p className="text-xs font-medium uppercase text-muted-foreground">{label}</p>
      <div className="mt-2 flex flex-wrap gap-1.5">
        {values.map((value) => (
          <span
            key={value}
            className="rounded-md border border-border bg-muted px-2 py-1 text-xs text-muted-foreground"
          >
            {formatContractLabel(value)}
          </span>
        ))}
      </div>
    </div>
  );
}

function ReadinessBadge({
  readiness,
}: {
  readiness: AxisPrimitiveContract['readiness'] | AxisConsumerContract['readiness'];
}) {
  return (
    <Badge variant={readiness === 'ready' ? 'success' : 'warning'}>
      {formatContractLabel(readiness)}
    </Badge>
  );
}

function pluralize(count: number, singular: string) {
  return `${count} ${singular}${count === 1 ? '' : 's'}`;
}

function ContractReferences({
  sourceFile,
  testFiles,
  visualTargets = [],
}: {
  sourceFile: string;
  testFiles: readonly string[];
  visualTargets?: readonly string[];
}) {
  const summary = [
    'source',
    pluralize(testFiles.length, 'test'),
    visualTargets.length > 0 ? pluralize(visualTargets.length, 'visual target') : null,
  ]
    .filter(Boolean)
    .join(' + ');

  return (
    <details className="pt-1 text-xs text-muted-foreground">
      <summary className="cursor-pointer font-medium text-muted-foreground hover:text-foreground">
        References ({summary})
      </summary>
      <div className="mt-3 space-y-3 border-l border-border pl-3">
        <div>
          <p className="font-medium uppercase text-muted-foreground">Source</p>
          <code className="mt-1 block break-all text-muted-foreground">{sourceFile}</code>
        </div>
        <div>
          <p className="font-medium uppercase text-muted-foreground">Tests</p>
          <div className="mt-1 space-y-1">
            {testFiles.map((testFile) => (
              <code key={testFile} className="block break-all text-muted-foreground">
                {testFile}
              </code>
            ))}
          </div>
        </div>
        {visualTargets.length > 0 ? (
          <div>
            <p className="font-medium uppercase text-muted-foreground">Visual targets</p>
            <div className="mt-1 space-y-1">
              {visualTargets.map((target) => (
                <code key={target} className="block break-all text-muted-foreground">
                  {target}
                </code>
              ))}
            </div>
          </div>
        ) : null}
      </div>
    </details>
  );
}

function PrimitiveReadinessMatrix() {
  return (
    <div className="grid gap-3">
      {axisPrimitiveContracts.map((contract) => (
        <div
          key={contract.component}
          className="grid gap-4 rounded-lg border border-border bg-card p-4 shadow-surface lg:grid-cols-[11rem_minmax(0,1fr)]"
          data-primitive-contract={contract.component}
        >
          <div className="space-y-2">
            <div className="flex flex-wrap items-center gap-2">
              <h3 className="text-sm font-semibold text-foreground">{contract.component}</h3>
              <ReadinessBadge readiness={contract.readiness} />
            </div>
            <p className="text-xs font-medium text-muted-foreground">Primitive contract</p>
            <div className="flex flex-wrap gap-1.5">
              <span className="rounded-md border border-border bg-muted px-2 py-1 text-xs text-muted-foreground">
                {pluralize(contract.testFiles.length, 'test')}
              </span>
              <span className="rounded-md border border-border bg-muted px-2 py-1 text-xs text-muted-foreground">
                {pluralize(contract.visualTargets.length, 'visual target')}
              </span>
            </div>
            <ContractReferences
              sourceFile={contract.file}
              testFiles={contract.testFiles}
              visualTargets={contract.visualTargets}
            />
          </div>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <ReadinessGroup label="Variants" values={contract.variants} />
            <ReadinessGroup label="States" values={contract.states} />
            <ReadinessGroup label="Accessibility" values={contract.accessibility} />
            <ReadinessGroup label="Tokens" values={contract.tokenFamilies} />
          </div>
        </div>
      ))}
    </div>
  );
}

function ConsumerReadinessMatrix() {
  return (
    <div className="grid gap-3">
      {axisConsumerContracts.map((contract) => (
        <div
          key={contract.component}
          className="grid gap-4 rounded-lg border border-border bg-card p-4 shadow-surface lg:grid-cols-[12rem_minmax(0,1fr)]"
          data-consumer-contract={contract.component}
        >
          <div className="space-y-2">
            <div className="flex flex-wrap items-center gap-2">
              <h3 className="text-sm font-semibold text-foreground">{contract.component}</h3>
              <ReadinessBadge readiness={contract.readiness} />
            </div>
            <p className="text-xs font-medium text-muted-foreground">{contract.surface}</p>
            <div className="flex flex-wrap gap-1.5">
              <span className="rounded-md border border-border bg-muted px-2 py-1 text-xs text-muted-foreground">
                {contract.route}
              </span>
              <span className="rounded-md border border-border bg-muted px-2 py-1 text-xs text-muted-foreground">
                {formatContractLabel(contract.owner)}
              </span>
              <span className="rounded-md border border-border bg-muted px-2 py-1 text-xs text-muted-foreground">
                {formatContractLabel(contract.kind)}
              </span>
            </div>
            <ContractReferences sourceFile={contract.file} testFiles={contract.testFiles} />
          </div>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <ReadinessGroup label="Primitives" values={contract.primitives} />
            <ReadinessGroup label="States" values={contract.states} />
            <ReadinessGroup label="Evidence" values={contract.evidence} />
            <ReadinessGroup
              label="Coverage"
              values={[pluralize(contract.testFiles.length, 'test file')]}
            />
          </div>
        </div>
      ))}
    </div>
  );
}

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
          className="overflow-hidden rounded-lg border border-border bg-card shadow-surface"
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
      <div className="space-y-3 rounded-lg border border-inverse-border bg-inverse p-4">
        <p className="text-sm font-medium text-inverse-foreground">Inverted surface</p>
        <ActionLink to="/" icon={ArrowRight} surface="inverted">
          Start flow
        </ActionLink>
        <ActionLink to="/" icon={ExternalLink} surface="inverted" variant="secondary">
          Learn more
        </ActionLink>
      </div>
      <div className="space-y-3 rounded-lg border border-border bg-card p-4 dark:border-inverse-border dark:bg-inverse">
        <p className="text-sm font-medium text-foreground dark:text-inverse-foreground">
          Adaptive surface
        </p>
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

function FeedbackMatrix() {
  return (
    <div className="space-y-5">
      <div className="grid gap-3 lg:grid-cols-2">
        <Notice variant="info" title="Information">
          Connection settings were saved and are ready for the next run.
        </Notice>
        <Notice variant="success" title="Ready">
          The workspace profile is active and available.
        </Notice>
        <Notice variant="warning" title="Needs review">
          Check the required fields before publishing this workflow.
        </Notice>
        <Notice variant="error" title="Unable to load">
          Retry after the service is reachable again.
        </Notice>
      </div>
      <div className="flex flex-wrap gap-2">
        <Badge variant="primary">Primary</Badge>
        <Badge variant="accent">Accent</Badge>
        <Badge variant="info">Info</Badge>
        <Badge variant="success">Success</Badge>
        <Badge variant="warning">Warning</Badge>
        <Badge variant="destructive">Danger</Badge>
        <Badge variant="outline">Outline</Badge>
      </div>
      <div className="grid gap-4 lg:grid-cols-2">
        <Panel className="space-y-3">
          <p className="text-sm font-medium text-foreground">Progress</p>
          <Progress value={62} aria-label="Catalog progress example" />
          <Progress value={100} isIndeterminate aria-label="Catalog indeterminate example" />
        </Panel>
        <Panel className="space-y-3">
          <p className="text-sm font-medium text-foreground">Skeleton</p>
          <Skeleton className="h-4 w-28" />
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-4 w-2/3" />
        </Panel>
      </div>
    </div>
  );
}

function StructureMatrix() {
  return (
    <div className="grid gap-4 lg:grid-cols-3">
      <Card>
        <CardHeader>
          <CardTitle>Card title</CardTitle>
          <CardDescription>Reusable card anatomy for compact work surfaces.</CardDescription>
        </CardHeader>
        <CardContent>
          <Badge variant="success">Stable</Badge>
        </CardContent>
      </Card>
      <Panel variant="muted" className="space-y-3">
        <Layers3 className="size-5 text-primary" aria-hidden />
        <p className="text-sm font-medium text-foreground">Muted panel</p>
        <p className="text-sm leading-6 text-muted-foreground">
          Panels frame operational state without page-local geometry.
        </p>
      </Panel>
      <EmptyState
        icon={Inbox}
        title="No records yet"
        description="Create the first item when the owning workflow is ready."
        action={
          <Button type="button" variant="outline" size="sm">
            <ArrowRight className="size-3.5" aria-hidden />
            Open
          </Button>
        }
      />
    </div>
  );
}

function LayoutMatrix() {
  return (
    <div className="space-y-4">
      <PageHeader
        eyebrow="Workspace"
        title="Operations overview"
        description="A compact header keeps the title, supporting state, and actions aligned."
        actions={
          <Button type="button" variant="outline" size="sm">
            <Settings2 className="size-3.5" aria-hidden />
            Configure
          </Button>
        }
      />
      <Toolbar>
        <Button type="button" variant="ghost" size="sm">
          <Search className="size-3.5" aria-hidden />
          Search
        </Button>
        <Button type="button" variant="ghost" size="sm">
          <AlertTriangle className="size-3.5" aria-hidden />
          Filter
        </Button>
        <IconButton
          type="button"
          icon={MoreHorizontal}
          label="More layout actions"
          size="icon-sm"
        />
      </Toolbar>
      <ContentGrid>
        <Panel variant="inset" className="p-4">
          <p className="text-sm font-medium text-foreground">First panel</p>
        </Panel>
        <Panel variant="inset" className="p-4">
          <p className="text-sm font-medium text-foreground">Second panel</p>
        </Panel>
        <Panel variant="inset" className="p-4">
          <p className="text-sm font-medium text-foreground">Third panel</p>
        </Panel>
      </ContentGrid>
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
            <SwatchGrid swatches={stateSwatches} />
            <SwatchGrid swatches={surfaceSwatches} />
          </div>
        </CatalogSection>

        <CatalogSection title="Primitive readiness" eyebrow="primitive-readiness">
          <PrimitiveReadinessMatrix />
        </CatalogSection>

        <CatalogSection title="Consumer contracts" eyebrow="consumer-readiness">
          <ConsumerReadinessMatrix />
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

        <CatalogSection title="Feedback" eyebrow="feedback">
          <FeedbackMatrix />
        </CatalogSection>

        <CatalogSection title="Structure and data" eyebrow="structure-data">
          <StructureMatrix />
        </CatalogSection>

        <CatalogSection title="Layout" eyebrow="layout">
          <LayoutMatrix />
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
