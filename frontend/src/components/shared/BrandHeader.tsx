type BrandHeaderLabelElement = 'p' | 'h1' | 'h2';

const labelStyles = {
  h1: 'font-heading text-2xl font-semibold text-foreground',
  h2: 'font-heading text-lg font-medium text-foreground',
  p: 'font-heading text-xs font-semibold uppercase tracking-widest text-muted-foreground',
} satisfies Record<BrandHeaderLabelElement, string>;

interface BrandHeaderProps {
  label?: string;
  labelElement?: BrandHeaderLabelElement;
}

function BrandHeader({ label, labelElement = 'p' }: BrandHeaderProps) {
  const LabelElement = labelElement;

  return (
    <div className="flex items-center gap-3 pb-2">
      <img src="/axis-logo.svg" alt="" className="size-11 shrink-0" width={44} height={44} />
      {label ? <LabelElement className={labelStyles[labelElement]}>{label}</LabelElement> : null}
    </div>
  );
}

export { BrandHeader };
