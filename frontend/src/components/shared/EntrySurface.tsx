import { Link } from '@tanstack/react-router';
import { ChevronDown, Settings2 } from 'lucide-react';
import { type ComponentProps, type ReactNode, useId } from 'react';
import { AsyncButton } from '@/components/shared/AsyncButton';
import { BrandHeader } from '@/components/shared/BrandHeader';
import { OptionList, OptionListItem } from '@/components/shared/OptionList';
import { EntryLayout } from '@/components/shared/PageLayout';
import { Button, buttonVariants } from '@/components/ui/button';
import { Card, CardContent, CardFooter, CardHeader } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
import { FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { type SurfaceIdFor, surfaceContractAttributes } from '@/lib/ui-foundation';
import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';

const entryControlHeight = cn(
  axisStyles.density.minHeight.touchTarget,
  axisStyles.density.minHeight.compactControlAtSmall,
);

const entryActionGeometry = cn(
  entryControlHeight,
  axisStyles.density.minWidth.touchTarget,
  axisStyles.density.minWidth.compactControlAtSmall,
  'w-full',
);

const entryUtilityTargetGeometry = cn(
  axisStyles.density.minHeight.touchTarget,
  axisStyles.density.minWidth.touchTarget,
  axisStyles.density.minHeight.compactControlAtSmall,
  axisStyles.density.minWidth.compactControlAtSmall,
);

interface EntryPreferenceOption {
  icon: ReactNode;
  label: string;
  value: string;
}

interface EntryPreferenceGroupModel {
  label: string;
  onSelect: (value: string) => void;
  options: readonly EntryPreferenceOption[];
  value: string;
}

interface EntryPreferencesModel {
  label: string;
  language: EntryPreferenceGroupModel;
  theme: EntryPreferenceGroupModel;
}

function EntryPreferenceGroup({ model }: { model: EntryPreferenceGroupModel }) {
  const labelId = useId();

  return (
    <section aria-labelledby={labelId} className={cn('grid', axisStyles.spacing.gap.inline)}>
      <div
        id={labelId}
        className={cn(
          'text-muted-foreground',
          axisStyles.spacing.padding.inline.inline,
          axisStyles.typography.scale.metadata,
          axisStyles.typography.weight.label,
        )}
      >
        {model.label}
      </div>
      <OptionList label={model.label} value={model.value} onValueChange={model.onSelect}>
        {model.options.map((option) => (
          <OptionListItem key={option.value} icon={option.icon} value={option.value}>
            {option.label}
          </OptionListItem>
        ))}
      </OptionList>
    </section>
  );
}

function EntryPreferences({ model }: { model: EntryPreferencesModel }) {
  return (
    <Popover>
      <PopoverTrigger
        render={
          <Button
            type="button"
            variant="outline"
            size="sm"
            className={entryUtilityTargetGeometry}
            aria-label={model.label}
            title={model.label}
          />
        }
      >
        <Settings2 aria-hidden />
        <span>{model.label}</span>
        <ChevronDown aria-hidden />
      </PopoverTrigger>
      <PopoverContent
        data-slot="entry-preferences"
        align="end"
        aria-label={model.label}
        className="max-h-(--available-height) w-80 max-w-(--available-width) gap-0 overflow-y-auto p-0"
      >
        <div
          data-slot="entry-preferences-groups"
          className={cn(
            'grid',
            axisStyles.spacing.gap.region,
            axisStyles.spacing.padding.all.region,
          )}
        >
          <EntryPreferenceGroup model={model.language} />
          <EntryPreferenceGroup model={model.theme} />
        </div>
      </PopoverContent>
    </Popover>
  );
}

type EntryActionProps = Omit<ComponentProps<typeof Button>, 'className'>;

function EntryAction(props: EntryActionProps) {
  return <Button {...props} className={entryActionGeometry} />;
}

type EntryActionLinkProps = Omit<ComponentProps<typeof Link>, 'className'> & {
  variant?: 'default' | 'outline';
};

function EntryActionLink({ variant = 'default', ...props }: EntryActionLinkProps) {
  return (
    <Link
      {...props}
      className={buttonVariants({ variant, size: 'lg', className: entryActionGeometry })}
    />
  );
}

type EntryAsyncActionProps = Omit<ComponentProps<typeof AsyncButton>, 'className'>;

function EntryAsyncAction(props: EntryAsyncActionProps) {
  return <AsyncButton {...props} className={entryActionGeometry} />;
}

type EntryInputProps = Omit<ComponentProps<typeof Input>, 'className'>;

function EntryInput(props: EntryInputProps) {
  return <Input {...props} className={entryControlHeight} />;
}

type EntryConsentLabelProps = Omit<ComponentProps<typeof FieldLabel>, 'className'>;

function EntryConsentLabel(props: EntryConsentLabelProps) {
  return (
    <FieldLabel
      {...props}
      data-slot="entry-consent-label"
      className={cn(entryControlHeight, 'items-start')}
    />
  );
}

type EntryConsentCheckboxProps = Omit<ComponentProps<typeof Checkbox>, 'className'>;

function EntryConsentCheckbox(props: EntryConsentCheckboxProps) {
  return <Checkbox {...props} className="mt-0.5" />;
}

interface EntrySurfaceProps {
  banner?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  preferences: EntryPreferencesModel;
  surfaceId: SurfaceIdFor<'entry-surface'>;
  title: string;
}

export function EntrySurface({
  banner,
  children,
  footer,
  preferences,
  surfaceId,
  title,
}: EntrySurfaceProps) {
  return (
    <EntryLayout utilities={<EntryPreferences model={preferences} />}>
      <Card
        {...surfaceContractAttributes('entry-surface', surfaceId)}
        data-slot="entry-surface"
        className="w-full"
      >
        <CardHeader>
          <BrandHeader label={title} labelElement="h1" />
        </CardHeader>
        <CardContent>
          <div className="space-y-6">
            {banner}
            {children}
          </div>
        </CardContent>
        {footer ? (
          <CardFooter className="justify-center">
            <div className="text-center text-xs text-muted-foreground">{footer}</div>
          </CardFooter>
        ) : null}
      </Card>
    </EntryLayout>
  );
}

export type { EntrySurfaceProps };
export {
  EntryAction,
  EntryActionLink,
  type EntryActionLinkProps,
  type EntryActionProps,
  EntryAsyncAction,
  type EntryAsyncActionProps,
  EntryConsentCheckbox,
  type EntryConsentCheckboxProps,
  EntryConsentLabel,
  type EntryConsentLabelProps,
  EntryInput,
  type EntryInputProps,
  type EntryPreferenceGroupModel,
  type EntryPreferenceOption,
  type EntryPreferencesModel,
};
