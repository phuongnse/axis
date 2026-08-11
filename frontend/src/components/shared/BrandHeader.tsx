import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';

type BrandHeaderLabelElement = 'p' | 'h1' | 'h2';

const labelStyles = {
  h1: cn(
    'font-heading text-foreground',
    axisStyles.typography.scale.pageTitle,
    axisStyles.typography.weight.pageTitle,
  ),
  h2: cn(
    'font-heading text-foreground',
    axisStyles.typography.scale.sectionTitle,
    axisStyles.typography.weight.sectionTitle,
  ),
  p: cn(
    'font-heading uppercase tracking-widest text-muted-foreground',
    axisStyles.typography.scale.metadata,
    axisStyles.typography.weight.label,
  ),
} satisfies Record<BrandHeaderLabelElement, string>;

interface BrandHeaderProps {
  label?: string;
  labelElement?: BrandHeaderLabelElement;
}

function BrandHeader({ label, labelElement = 'p' }: BrandHeaderProps) {
  const LabelElement = labelElement;

  return (
    <div
      className={cn(
        'flex items-center',
        axisStyles.spacing.gap.region,
        axisStyles.spacing.padding.bottom.inline,
      )}
    >
      <img src="/axis-logo.svg" alt="" className="size-11 shrink-0" width={44} height={44} />
      {label ? <LabelElement className={labelStyles[labelElement]}>{label}</LabelElement> : null}
    </div>
  );
}

export { BrandHeader };
