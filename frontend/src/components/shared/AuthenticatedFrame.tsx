import type { ReactNode } from 'react';
import { type SurfaceIdFor, surfaceContractAttributes } from '@/lib/ui-foundation';

export interface AuthenticatedFrameProps {
  children: ReactNode;
  contentBlocked?: boolean;
  contentBusy?: boolean;
  contentObscured?: boolean;
  contextSurface?: ReactNode;
  contextSurfaceVisible?: boolean;
  footer: ReactNode;
  header: ReactNode;
  managedWindows: ReactNode;
  navigation: ReactNode;
  notifications: ReactNode;
  surfaceId: SurfaceIdFor<'authenticated-frame'>;
}

export function AuthenticatedFrame({
  children,
  contentBlocked = false,
  contentBusy = false,
  contentObscured = false,
  contextSurface,
  contextSurfaceVisible = false,
  footer,
  header,
  managedWindows,
  navigation,
  notifications,
  surfaceId,
}: AuthenticatedFrameProps) {
  return (
    <div
      {...surfaceContractAttributes('authenticated-frame', surfaceId)}
      className="flex h-dvh min-h-0 flex-col overflow-hidden bg-background text-foreground"
    >
      {header}
      <div data-slot="authenticated-work-area" className="relative min-h-0 min-w-0 flex-1">
        <div className="flex h-full min-h-0 min-w-0 flex-col md:flex-row">
          <div data-slot="module-navigation-boundary" className="contents" inert={contentBlocked}>
            {navigation}
          </div>
          <main
            className="relative flex min-h-0 w-full min-w-0 flex-1 overflow-hidden bg-background"
            aria-busy={contentBusy || undefined}
          >
            <div
              data-slot="authenticated-route-content"
              className={`flex h-full min-h-0 w-full min-w-0 flex-1 ${
                contentObscured ? 'invisible pointer-events-none' : ''
              }`}
              inert={contentBlocked}
            >
              {children}
            </div>
            {contextSurfaceVisible ? (
              <div
                data-slot="workspace-context-surface"
                className="absolute inset-0 flex items-center justify-center overflow-hidden bg-background p-6"
                aria-live="polite"
              >
                {contextSurface}
              </div>
            ) : null}
          </main>
        </div>
        {managedWindows}
        {notifications}
      </div>
      {footer}
    </div>
  );
}
