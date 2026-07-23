import { Layers3Icon, Maximize2Icon, XIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  opaquePopoverTriggerSurface,
  persistentItemHighlight,
} from '@/components/shared/interactionStates';
import { ManagedDialog, ManagedDialogBody } from '@/components/shared/ManagedDialog';
import {
  type ManagedWindowEntry,
  ManagedWindowRendererScope,
  useManagedWindowActions,
  useManagedWindowHostContext,
  useManagedWindowStore,
} from '@/components/shared/ManagedWindowManager';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

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
  const hasWindows = entries.length > 0;

  return (
    <div
      ref={setHost}
      data-slot="managed-window-host"
      className="pointer-events-none absolute inset-0"
    >
      <div
        ref={manager.setPortalContainer}
        data-slot="managed-window-expanded-layer"
        className="pointer-events-none absolute inset-0 z-40 overflow-hidden"
      />

      {entries.map(([windowId, entry]) => {
        const Renderer = manager.renderers[entry.descriptor.kind];
        return (
          <ManagedWindowRendererScope key={windowId} windowId={windowId}>
            {Renderer ? (
              <Renderer descriptor={entry.descriptor} />
            ) : (
              <ManagedDialog
                open
                title={entry.title}
                description={t('dialog.unavailableDescription')}
                onOpenChange={(open) => {
                  if (!open) closeWindow(windowId);
                }}
                footer={
                  <Button type="button" variant="outline" onClick={() => closeWindow(windowId)}>
                    {t('app.close')}
                  </Button>
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

      {hasWindows ? (
        <div
          data-slot="managed-window-tray"
          className="pointer-events-none absolute inset-x-3 bottom-2 z-50 flex h-12 min-w-0 items-stretch justify-end gap-2"
        >
          <WindowMenu
            label={t('dialog.windows', { count: entries.length })}
            entries={entries}
            activeWindowId={activeWindowId}
            onSelect={(windowId, entry) => {
              if (entry.mode === 'docked') restoreWindow(windowId);
              else focusWindow(windowId);
            }}
          />

          {hiddenDocks.length > 0 ? (
            <WindowMenu
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
              className="pointer-events-auto flex min-w-0 max-w-64 flex-1 items-center overflow-hidden rounded-xl bg-popover text-sm text-popover-foreground shadow-xl ring-1 ring-foreground/10 sm:w-64 sm:flex-none"
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
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                aria-label={t('dialog.restore')}
                title={t('dialog.restore')}
                onClick={() => restoreWindow(windowId)}
              >
                <Maximize2Icon />
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                disabled={entry.busy}
                aria-label={t('dialog.close')}
                title={t('dialog.close')}
                onClick={() => requestClose(windowId)}
              >
                <XIcon />
              </Button>
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function WindowMenu({
  label,
  compactLabel,
  entries,
  activeWindowId,
  onSelect,
}: {
  label: string;
  compactLabel?: string;
  entries: readonly (readonly [string, ManagedWindowEntry])[];
  activeWindowId: string | null;
  onSelect: (windowId: string, entry: ManagedWindowEntry) => void;
}) {
  const { t } = useTranslation();
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        aria-label={compactLabel ? label : undefined}
        title={compactLabel ? label : undefined}
        render={
          <Button
            type="button"
            variant="outline"
            className={`pointer-events-auto h-full shrink-0 ${opaquePopoverTriggerSurface}`}
          />
        }
      >
        {compactLabel ? null : <Layers3Icon />}
        <span>{compactLabel ?? label}</span>
      </DropdownMenuTrigger>
      <DropdownMenuContent side="top" align="end" className="w-72">
        <DropdownMenuGroup>
          <DropdownMenuLabel>{label}</DropdownMenuLabel>
          {entries.map(([windowId, entry]) => {
            const active = windowId === activeWindowId;
            return (
              <DropdownMenuItem
                key={windowId}
                aria-current={active ? 'true' : undefined}
                className={active ? persistentItemHighlight : undefined}
                onClick={() => onSelect(windowId, entry)}
              >
                <span className="min-w-0 flex-1 truncate">{entry.title}</span>
                {entry.dirty ? (
                  <span data-slot="managed-window-dirty-indicator" title={t('dialog.unsaved')}>
                    <span aria-hidden="true">•</span>
                    <span className="sr-only">{t('dialog.unsaved')}</span>
                  </span>
                ) : null}
                <span className="text-xs text-muted-foreground">
                  {entry.mode === 'docked' ? t('dialog.minimized') : t('dialog.expanded')}
                </span>
              </DropdownMenuItem>
            );
          })}
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
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
