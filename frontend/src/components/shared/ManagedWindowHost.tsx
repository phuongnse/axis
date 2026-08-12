import { Maximize2Icon, XIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ManagedDialog,
  ManagedDialogAction,
  ManagedDialogBody,
  ManagedDialogIconAction,
  ManagedWindowMenu,
} from '@/components/shared/ManagedDialog';
import {
  ManagedWindowRendererScope,
  useManagedWindowActions,
  useManagedWindowHostContext,
  useManagedWindowStore,
} from '@/components/shared/ManagedWindowManager';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';

export function ManagedWindowHost() {
  const { t } = useTranslation();
  const manager = useManagedWindowHostContext();
  const windows = useManagedWindowStore((state) => state.windows);
  const windowOrder = useManagedWindowStore((state) => state.windowOrder);
  const activeWindowId = useManagedWindowStore((state) => state.activeWindowId);
  const { closeWindow, focusWindow, requestClose, restoreWindow } = useManagedWindowActions();
  const [host, setHost] = useState<HTMLDivElement | null>(null);
  const hostWidth = useElementWidth(host);
  const entries = windowOrder.flatMap((windowId) => {
    const entry = windows[windowId];
    return entry ? [[windowId, entry] as const] : [];
  });
  const docked = entries
    .filter(([, entry]) => entry.mode === 'docked')
    .sort((left, right) => left[1].dockOrder - right[1].dockOrder);
  const visibleDockCount =
    hostWidth < 640 ? 1 : Math.max(1, Math.min(3, Math.floor((hostWidth - 240) / 272)));
  const visibleDocks = docked.slice(-visibleDockCount);
  const hiddenDocks = docked.slice(0, Math.max(0, docked.length - visibleDockCount));
  const hasExpandedWindow = entries.some(([, entry]) => entry.mode === 'expanded');

  return (
    <div
      ref={setHost}
      data-slot="managed-window-host"
      className="pointer-events-none absolute inset-0"
    >
      <div
        ref={manager.setPortalContainer}
        data-slot="managed-window-expanded-layer"
        className={cn(
          'pointer-events-none absolute inset-0 overflow-hidden',
          axisStyles.layer.modal,
        )}
      />

      {entries.map(([windowId, entry]) => {
        const Renderer = manager.renderers[entry.descriptor.kind];
        return (
          <ManagedWindowRendererScope key={windowId} windowId={windowId}>
            {Renderer ? (
              <Renderer descriptor={entry.descriptor} />
            ) : (
              <ManagedDialog
                surfaceId="managed-window-host"
                open
                title={entry.title}
                description={t('dialog.unavailableDescription')}
                onOpenChange={(open) => {
                  if (!open) closeWindow(windowId);
                }}
                footer={
                  <ManagedDialogAction
                    type="button"
                    variant="outline"
                    onClick={() => closeWindow(windowId)}
                  >
                    {t('app.close')}
                  </ManagedDialogAction>
                }
              >
                <ManagedDialogBody>
                  <p role="alert" className="text-sm text-muted-foreground">
                    {t('dialog.unavailable')}
                  </p>
                </ManagedDialogBody>
              </ManagedDialog>
            )}
          </ManagedWindowRendererScope>
        );
      })}

      {docked.length > 0 ? (
        <div
          data-slot="managed-window-tray"
          className={cn(
            'pointer-events-none absolute inset-x-3 bottom-2 flex h-12 min-w-0 items-stretch justify-end',
            axisStyles.layer.managed,
            axisStyles.spacing.gap.inline,
          )}
        >
          {!hasExpandedWindow ? (
            <ManagedWindowMenu
              label={t('dialog.windows', { count: entries.length })}
              entries={entries}
              activeWindowId={activeWindowId}
              onSelect={(windowId, entry) => {
                if (entry.mode === 'docked') restoreWindow(windowId);
                else focusWindow(windowId);
              }}
            />
          ) : null}

          {hiddenDocks.length > 0 ? (
            <ManagedWindowMenu
              label={t('dialog.moreWindows', { count: hiddenDocks.length })}
              compactLabel={`+${hiddenDocks.length}`}
              entries={hiddenDocks}
              activeWindowId={activeWindowId}
              onSelect={(windowId) => restoreWindow(windowId)}
            />
          ) : null}

          {visibleDocks.map(([windowId, entry]) => (
            <div
              key={windowId}
              data-slot="managed-window-dock"
              data-window-id={windowId}
              data-dialog-preset={entry.preset}
              className={cn(
                'pointer-events-auto flex min-w-0 max-w-64 flex-1 items-center overflow-hidden bg-popover text-popover-foreground ring-1 ring-foreground/10 sm:w-64 sm:flex-none',
                axisStyles.radius.managed,
                axisStyles.typography.scale.body,
                axisStyles.typography.weight.body,
                axisStyles.elevation.dock,
              )}
            >
              <Button
                data-action="restore"
                type="button"
                variant="ghost"
                className="h-full min-w-0 flex-1 justify-start rounded-none px-3"
                title={t('dialog.restore')}
                onClick={() => restoreWindow(windowId)}
              >
                <span className="truncate font-medium">{entry.title}</span>
                {entry.dirty ? (
                  <span data-slot="managed-window-dirty-indicator" title={t('dialog.unsaved')}>
                    <span aria-hidden="true">•</span>
                    <span className="sr-only">{t('dialog.unsaved')}</span>
                  </span>
                ) : null}
              </Button>
              <ManagedDialogIconAction
                type="button"
                variant="ghost"
                size="icon-sm"
                aria-label={t('dialog.restore')}
                title={t('dialog.restore')}
                onClick={() => restoreWindow(windowId)}
              >
                <Maximize2Icon />
              </ManagedDialogIconAction>
              <ManagedDialogIconAction
                type="button"
                variant="ghost"
                size="icon-sm"
                disabled={entry.busy}
                aria-label={t('dialog.close')}
                title={t('dialog.close')}
                onClick={() => requestClose(windowId)}
              >
                <XIcon />
              </ManagedDialogIconAction>
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function useElementWidth(element: HTMLElement | null) {
  const [width, setWidth] = useState(() =>
    typeof window === 'undefined' ? 1024 : window.innerWidth,
  );

  useEffect(() => {
    if (!element) return;
    const update = () => setWidth(element.clientWidth || window.innerWidth);
    update();
    if (typeof ResizeObserver === 'undefined') {
      window.addEventListener('resize', update);
      return () => window.removeEventListener('resize', update);
    }
    const observer = new ResizeObserver(update);
    observer.observe(element);
    return () => observer.disconnect();
  }, [element]);

  return width;
}
