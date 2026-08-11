import type { ReactNode } from 'react';
import { BrandHeader } from '@/components/shared/BrandHeader';
import { EntryLayout } from '@/components/shared/PageLayout';
import { Card, CardContent, CardFooter, CardHeader } from '@/components/ui/card';
import { type SurfaceIdFor, surfaceContractAttributes } from '@/lib/ui-foundation';

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
