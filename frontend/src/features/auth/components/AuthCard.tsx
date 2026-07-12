import type { ReactNode } from 'react';
import { BrandHeader } from '@/components/shared/BrandHeader';
import { Card, CardContent, CardFooter, CardHeader } from '@/components/ui/card';
import { PreferencesMenu } from '@/features/preferences';

interface AuthCardProps {
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  banner?: ReactNode;
}

export function AuthCard({ title, children, footer, banner }: AuthCardProps) {
  return (
    <div className="flex min-h-screen flex-col bg-background p-4 sm:p-6">
      <div className="self-end">
        <PreferencesMenu />
      </div>
      <div className="mx-auto flex w-full max-w-lg flex-1 items-center justify-center">
        <Card className="w-full">
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
      </div>
    </div>
  );
}
