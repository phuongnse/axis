import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';

const dataTableControlHeight = cn(
  axisStyles.density.minHeight.touchTarget,
  axisStyles.density.minHeight.compactControlAtSmall,
);

const dataTableTargetGeometry = cn(
  dataTableControlHeight,
  axisStyles.density.minWidth.touchTarget,
  axisStyles.density.minWidth.compactControlAtSmall,
);

const dataTableCheckboxHitArea = 'after:-inset-3.5 sm:after:-inset-2';

export { dataTableCheckboxHitArea, dataTableControlHeight, dataTableTargetGeometry };
