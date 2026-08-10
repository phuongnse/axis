type BrandHeaderLabelElement = 'p' | 'h1' | 'h2';

const labelStyles = {
  h1: 'font-heading text-axis-page-title font-axis-page-title text-foreground',
  h2: 'font-heading text-axis-section-title font-axis-section-title text-foreground',
  p: 'font-heading text-axis-metadata font-axis-label uppercase tracking-widest text-muted-foreground',
} satisfies Record<BrandHeaderLabelElement, string>;

interface BrandHeaderProps {
  label?: string;
  labelElement?: BrandHeaderLabelElement;
}

function BrandHeader({ label, labelElement = 'p' }: BrandHeaderProps) {
  const LabelElement = labelElement;

  return (
    <div className="flex items-center gap-axis-region pb-axis-inline">
      <img src="/axis-logo.svg" alt="" className="size-11 shrink-0" width={44} height={44} />
      {label ? <LabelElement className={labelStyles[labelElement]}>{label}</LabelElement> : null}
    </div>
  );
}

export { BrandHeader };
