import type { ComponentProps } from 'react';

import { Badge } from '@/components/ui/badge';

type MetadataTagProps = Omit<ComponentProps<typeof Badge>, 'className' | 'variant'>;

function MetadataTag(props: MetadataTagProps) {
  return <Badge {...props} variant="secondary" />;
}

export { MetadataTag };
