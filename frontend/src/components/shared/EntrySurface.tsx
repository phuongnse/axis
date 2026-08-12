import { Link } from '@tanstack/react-router';
import type { ComponentProps, ReactNode } from 'react';
import { AsyncButton } from '@/components/shared/AsyncButton';
import { BrandHeader } from '@/components/shared/BrandHeader';
import { EntryLayout } from '@/components/shared/PageLayout';
import { Button, buttonVariants } from '@/components/ui/button';
import { Card, CardContent, CardFooter, CardHeader } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
import { FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
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
  surfaceId: SurfaceIdFor<'entry-surface'>;
  title: string;
  utilities: ReactNode;
}

export function EntrySurface({
  banner,
  children,
  footer,
  surfaceId,
  title,
  utilities,
}: EntrySurfaceProps) {
  return (
    <EntryLayout utilities={utilities}>
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
};
