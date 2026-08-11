import { type ClassValue, clsx } from 'clsx';
import { extendTailwindMerge } from 'tailwind-merge';

import { axisTailwindMergeExtension } from '@/theme.generated';

const mergeAxisClassNames = extendTailwindMerge(axisTailwindMergeExtension);

export function cn(...inputs: ClassValue[]) {
  return mergeAxisClassNames(clsx(inputs));
}
