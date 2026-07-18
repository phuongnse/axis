type BrandHeaderLabelElement = 'p' | 'h1' | 'h2';

interface BrandHeaderProps {
  label?: string;
  labelElement?: BrandHeaderLabelElement;
}

function BrandHeader({ label, labelElement = 'p' }: BrandHeaderProps) {
  const LabelElement = labelElement;

  return (
    <div className="flex items-center gap-3 pb-2">
      <img src="/axis-logo.svg" alt="" className="size-11 shrink-0" width={44} height={44} />
      {label ? (
        <LabelElement className="font-heading text-xs font-semibold uppercase tracking-widest text-muted-foreground">
          {label}
        </LabelElement>
      ) : null}
    </div>
  );
}

export { BrandHeader };
