import axisLogo from '@/assets/axis-logo.svg';
import { HeaderRule } from '@/components/visual/HeaderRule';
import { cn } from '@/lib/utils';

type BrandHeaderTone = 'default' | 'inverted' | 'adaptive';
type BrandHeaderLabelElement = 'p' | 'h1' | 'h2';

interface BrandHeaderProps {
  label?: string;
  labelElement?: BrandHeaderLabelElement;
  tone?: BrandHeaderTone;
}

const labelToneClass: Record<BrandHeaderTone, string> = {
  default: 'text-muted-foreground',
  inverted: 'text-inverse-muted',
  adaptive: 'text-muted-foreground dark:text-inverse-muted',
};

function BrandHeader({ label, labelElement = 'p', tone = 'default' }: BrandHeaderProps) {
  const labelClassName = cn('text-xs uppercase tracking-[0.18em]', labelToneClass[tone]);
  const LabelElement = labelElement;

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <img src={axisLogo} alt="Axis" className="size-11 shrink-0" width={44} height={44} />
        {label ? <LabelElement className={labelClassName}>{label}</LabelElement> : null}
      </div>
      <HeaderRule tone={tone} />
    </div>
  );
}

export { BrandHeader };
