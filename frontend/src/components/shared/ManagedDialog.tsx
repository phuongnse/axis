import {
  Layers3Icon,
  Maximize2Icon,
  Minimize2Icon,
  MinusIcon,
  RotateCcwIcon,
  XIcon,
} from 'lucide-react';
import {
  type ComponentProps,
  createContext,
  type KeyboardEvent as ReactKeyboardEvent,
  type MouseEvent as ReactMouseEvent,
  type ReactNode,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { useTranslation } from 'react-i18next';
import { type DraggableData, Rnd } from 'react-rnd';
import { AsyncButton } from '@/components/shared/AsyncButton';
import {
  opaquePopoverTriggerSurface,
  persistentItemHighlight,
} from '@/components/shared/interactionStates';
import {
  type ManagedWindowEntry,
  type ManagedWindowPreset,
  type ManagedWindowRect,
  useManagedWindowActions,
  useManagedWindowRuntime,
  useManagedWindowStore,
} from '@/components/shared/ManagedWindowManager';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogDescription,
  DialogPopup,
  DialogPortal,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { type SurfaceIdFor, surfaceContractAttributes } from '@/lib/ui-foundation';
import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';

const COMPACT_VIEWPORT_WIDTH = 640;
const WINDOWED_DIALOG_WIDTH_SCALE = 0.5;
const WINDOWED_DIALOG_HEIGHT_SCALE = 0.75;
const MINIMUM_DIALOG_WIDTH_SCALE = 0.35;
const MINIMUM_DIALOG_HEIGHT_SCALE = 0.5;
const MANAGED_DIALOG_HEADER_SELECTOR = '[data-slot="managed-dialog-header"]';
const MANAGED_DIALOG_INTERACTIVE_SELECTOR =
  "button, a, input, textarea, select, [role='button'], [role='combobox']";

type WorkArea = {
  width: number;
  height: number;
};

const ManagedDialogFullscreenContext = createContext(false);

const managedDialogControlGeometry = cn(
  axisStyles.density.minHeight.touchTarget,
  axisStyles.density.minWidth.touchTarget,
  axisStyles.density.minHeight.compactControlAtSmall,
  axisStyles.density.minWidth.compactControlAtSmall,
);

type ManagedDialogActionProps = Omit<ComponentProps<typeof Button>, 'className'>;

function ManagedDialogAction(props: ManagedDialogActionProps) {
  return <Button {...props} className={managedDialogControlGeometry} />;
}

type ManagedDialogAsyncActionProps = Omit<ComponentProps<typeof AsyncButton>, 'className'>;

function ManagedDialogAsyncAction(props: ManagedDialogAsyncActionProps) {
  return <AsyncButton {...props} className={managedDialogControlGeometry} />;
}

type ManagedDialogIconActionProps = Omit<ComponentProps<typeof Button>, 'className'>;

function ManagedDialogIconAction(props: ManagedDialogIconActionProps) {
  return <Button {...props} className={managedDialogControlGeometry} />;
}

export interface ManagedDialogProps {
  children: ReactNode;
  closeDisabled?: boolean;
  description?: ReactNode;
  dirty?: boolean;
  footer: ReactNode;
  onOpenChange: (open: boolean) => void;
  open: boolean;
  surfaceId: SurfaceIdFor<'managed-task-window'>;
  title: string;
  titleAccessory?: ReactNode;
}

export function ManagedDialog({
  open,
  surfaceId,
  onOpenChange,
  title,
  description,
  titleAccessory,
  children,
  footer,
  closeDisabled = false,
  dirty = false,
}: ManagedDialogProps) {
  const { t } = useTranslation();
  const {
    windowId,
    entry,
    active,
    portalContainer,
    updateWindow,
    updateRuntime,
    focusWindow,
    minimizeWindow,
    registerCloseRequest,
    requestClose,
  } = useManagedWindowRuntime();
  const windows = useManagedWindowStore((state) => state.windows);
  const windowOrder = useManagedWindowStore((state) => state.windowOrder);
  const activeWindowId = useManagedWindowStore((state) => state.activeWindowId);
  const { restoreWindow } = useManagedWindowActions();
  const windowEntries = windowOrder.flatMap((candidateWindowId) => {
    const candidateEntry = windows[candidateWindowId];
    return candidateEntry ? [[candidateWindowId, candidateEntry] as const] : [];
  });
  const [workArea, setWorkArea] = useState(() => readWorkArea(portalContainer));
  const expandedFocusRef = useRef<HTMLElement | null>(null);
  const previousModeRef = useRef(entry?.mode);
  const requestConsumerClose = useCallback(() => onOpenChange(false), [onOpenChange]);

  useEffect(() => {
    updateRuntime(windowId, { title, dirty, busy: closeDisabled });
  }, [closeDisabled, dirty, title, updateRuntime, windowId]);

  useEffect(() => {
    registerCloseRequest(windowId, requestConsumerClose);
    return () => registerCloseRequest(windowId, null);
  }, [registerCloseRequest, requestConsumerClose, windowId]);

  useEffect(() => {
    if (!portalContainer) return;
    const update = () => setWorkArea(readWorkArea(portalContainer));
    update();
    if (typeof ResizeObserver === 'undefined') {
      window.addEventListener('resize', update);
      return () => window.removeEventListener('resize', update);
    }
    const observer = new ResizeObserver(update);
    observer.observe(portalContainer);
    return () => observer.disconnect();
  }, [portalContainer]);

  const isCompact = workArea.width < COMPACT_VIEWPORT_WIDTH;
  const configuredPreset = entry?.descriptor.initialSize ?? 'windowed';
  const effectivePreset = isCompact ? 'fullscreen' : (entry?.preset ?? configuredPreset);
  const effectiveRect = useMemo(
    () =>
      isCompact || effectivePreset === 'fullscreen'
        ? fullscreenRect(workArea)
        : clampRect(
            entry?.rect ??
              rectForPreset(workArea, effectivePreset === 'custom' ? 'windowed' : effectivePreset),
            workArea,
          ),
    [effectivePreset, entry?.rect, isCompact, workArea],
  );
  const minimumRectSize = minimumSize(workArea);

  useEffect(() => {
    if (!entry || isCompact || workArea.width <= 0 || workArea.height <= 0) return;
    if (
      entry.rect?.x === effectiveRect.x &&
      entry.rect.y === effectiveRect.y &&
      entry.rect.width === effectiveRect.width &&
      entry.rect.height === effectiveRect.height &&
      entry.preset === effectivePreset
    )
      return;
    updateWindow(windowId, { rect: effectiveRect, preset: effectivePreset });
  }, [
    effectivePreset,
    effectiveRect,
    entry,
    isCompact,
    updateWindow,
    windowId,
    workArea.height,
    workArea.width,
  ]);

  useEffect(() => {
    if (!entry || previousModeRef.current === entry.mode) return;
    previousModeRef.current = entry.mode;
    if (entry.mode === 'docked') {
      window.setTimeout(() => {
        findWindowElement('managed-window-dock', windowId)
          ?.querySelector<HTMLElement>('[data-action="restore"]')
          ?.focus();
      });
      return;
    }
    window.setTimeout(() => {
      const preferred = expandedFocusRef.current;
      if (preferred?.isConnected) preferred.focus();
      else
        findWindowElement('managed-dialog-window', windowId)
          ?.querySelector<HTMLElement>('button:not(:disabled), input:not(:disabled)')
          ?.focus();
    });
  }, [entry, windowId]);

  useEffect(() => {
    if (!active || entry?.mode !== 'expanded') return;
    window.setTimeout(() => {
      const windowElement = findWindowElement('managed-dialog-window', windowId);
      if (!windowElement || windowElement.contains(document.activeElement)) return;
      firstFocusable(windowElement)?.focus();
    });
  }, [active, entry?.mode, windowId]);

  if (!entry || !portalContainer) return null;

  const configuredFullscreenSnapshot =
    entry.preset === 'fullscreen' && configuredPreset === 'fullscreen'
      ? { preset: 'windowed' as const, rect: centeredWindowedRect(workArea) }
      : null;
  const restoreSnapshot = entry.maximizeSnapshot ?? configuredFullscreenSnapshot;
  const canRestoreSize = !isCompact && entry.preset === 'fullscreen' && restoreSnapshot !== null;
  const showingFullscreen = effectivePreset === 'fullscreen';
  const sizeActionLabel = showingFullscreen ? t('dialog.restoreSize') : t('dialog.maximize');

  function minimize() {
    expandedFocusRef.current =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
    minimizeWindow(windowId);
  }

  function toggleMaximize() {
    if (!entry) return;
    if (entry.preset === 'fullscreen' && restoreSnapshot) {
      updateWindow(windowId, {
        preset: restoreSnapshot.preset,
        rect: clampRect(restoreSnapshot.rect, workArea),
        maximizeSnapshot: null,
      });
      return;
    }
    updateWindow(windowId, {
      preset: 'fullscreen',
      rect: fullscreenRect(workArea),
      maximizeSnapshot: { preset: entry.preset, rect: effectiveRect },
    });
  }

  function reset() {
    const restoreRect = centeredWindowedRect(workArea);
    updateWindow(windowId, {
      preset: configuredPreset,
      rect: rectForPreset(workArea, configuredPreset),
      maximizeSnapshot:
        configuredPreset === 'fullscreen' ? { preset: 'windowed', rect: restoreRect } : null,
    });
  }

  function handleDragStop(_event: unknown, data: DraggableData) {
    updateWindow(windowId, {
      rect: clampPosition({ ...effectiveRect, x: data.x, y: data.y }, workArea),
    });
  }

  function handleResizeStop(element: HTMLElement, position: { x: number; y: number }) {
    const nextRect = clampRect(
      {
        width: element.offsetWidth,
        height: element.offsetHeight,
        ...position,
      },
      workArea,
    );
    updateWindow(windowId, {
      rect: nextRect,
      preset: isFullscreen(nextRect, workArea)
        ? 'fullscreen'
        : isWindowedSize(nextRect, workArea)
          ? 'windowed'
          : 'custom',
      maximizeSnapshot: null,
    });
  }

  const expanded = entry.mode === 'expanded';

  return (
    <Dialog
      open={open}
      modal={false}
      disablePointerDismissal
      onOpenChange={(nextOpen) => {
        if (!nextOpen && expanded && active) requestClose(windowId);
      }}
    >
      <DialogPortal container={portalContainer} className="pointer-events-auto">
        <DialogPopup
          data-slot="dialog-content"
          aria-hidden={expanded ? undefined : true}
          className={cn(
            'pointer-events-none absolute inset-0 h-full w-full overflow-hidden outline-none',
            !expanded && 'invisible',
          )}
          style={{ zIndex: entry.zIndex }}
        >
          <Rnd
            {...surfaceContractAttributes('managed-task-window', surfaceId)}
            data-slot="managed-dialog-window"
            data-window-id={windowId}
            data-dialog-preset={effectivePreset}
            data-active={active || undefined}
            bounds="parent"
            size={{ width: effectiveRect.width, height: effectiveRect.height }}
            position={{ x: effectiveRect.x, y: effectiveRect.y }}
            minWidth={minimumRectSize.width}
            minHeight={minimumRectSize.height}
            maxWidth={workArea.width}
            maxHeight={workArea.height}
            dragHandleClassName="managed-dialog-drag-handle"
            cancel={MANAGED_DIALOG_INTERACTIVE_SELECTOR}
            disableDragging={isCompact || effectivePreset === 'fullscreen'}
            enableResizing={!isCompact && effectivePreset !== 'fullscreen'}
            onPointerDownCapture={() => focusWindow(windowId)}
            onKeyDownCapture={(event: ReactKeyboardEvent<HTMLDivElement>) => {
              if (active) trapFocus(event);
            }}
            onDragStop={handleDragStop}
            onResizeStop={(_event, _direction, element, _delta, position) =>
              handleResizeStop(element, position)
            }
            onDoubleClick={(event: ReactMouseEvent<HTMLDivElement>) => {
              if (isCompact || !(event.target instanceof Element)) return;
              if (
                !event.target.closest(MANAGED_DIALOG_HEADER_SELECTOR) ||
                event.target.closest(MANAGED_DIALOG_INTERACTIVE_SELECTOR)
              )
                return;
              event.preventDefault();
              toggleMaximize();
            }}
            className={cn(
              'pointer-events-auto overflow-hidden bg-popover text-popover-foreground ring-1 ring-foreground/10',
              axisStyles.radius.managed,
              axisStyles.typography.scale.body,
              axisStyles.typography.weight.body,
              axisStyles.elevation.managed,
            )}
            style={{ display: 'flex', flexDirection: 'column' }}
          >
            <div
              data-slot="managed-dialog-header"
              className="managed-dialog-drag-handle flex shrink-0 cursor-default select-none flex-col gap-1 border-b p-4 sm:cursor-move"
            >
              <div
                data-slot="managed-dialog-header-primary"
                className="flex min-w-0 flex-col gap-2 sm:flex-row sm:items-center sm:justify-between sm:gap-4"
              >
                <div
                  data-slot="managed-dialog-header-identity"
                  className="flex min-w-0 flex-wrap items-center gap-2"
                >
                  <DialogTitle>{title}</DialogTitle>
                  {titleAccessory}
                  {dirty ? <span className="sr-only">{t('dialog.unsaved')}</span> : null}
                </div>
                <div
                  data-slot="managed-dialog-header-controls"
                  className="flex shrink-0 items-center gap-1 self-center sm:self-center"
                >
                  <ManagedDialogIconAction
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    aria-label={t('dialog.reset')}
                    title={t('dialog.reset')}
                    onClick={reset}
                  >
                    <RotateCcwIcon />
                  </ManagedDialogIconAction>
                  <ManagedDialogIconAction
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    aria-label={t('dialog.minimize')}
                    title={t('dialog.minimize')}
                    onClick={minimize}
                  >
                    <MinusIcon />
                  </ManagedDialogIconAction>
                  <ManagedDialogIconAction
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    aria-label={sizeActionLabel}
                    title={sizeActionLabel}
                    disabled={isCompact || (entry.preset === 'fullscreen' && !canRestoreSize)}
                    onClick={toggleMaximize}
                  >
                    {showingFullscreen ? <Minimize2Icon /> : <Maximize2Icon />}
                  </ManagedDialogIconAction>
                  <ManagedDialogIconAction
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    disabled={closeDisabled}
                    aria-label={t('dialog.close')}
                    title={t('dialog.close')}
                    onClick={() => requestClose(windowId)}
                  >
                    <XIcon />
                  </ManagedDialogIconAction>
                </div>
              </div>
              {description ? (
                <DialogDescription className="min-w-0">{description}</DialogDescription>
              ) : null}
            </div>
            <ManagedDialogFullscreenContext.Provider value={effectivePreset === 'fullscreen'}>
              {children}
              <ManagedDialogFooter
                switcher={
                  active ? (
                    <ManagedWindowMenu
                      label={t('dialog.windows', { count: windowEntries.length })}
                      entries={windowEntries}
                      activeWindowId={activeWindowId}
                      contentAlign="start"
                      onSelect={(candidateWindowId, candidateEntry) => {
                        if (candidateEntry.mode === 'docked') restoreWindow(candidateWindowId);
                        else focusWindow(candidateWindowId);
                      }}
                    />
                  ) : null
                }
              >
                {footer}
              </ManagedDialogFooter>
            </ManagedDialogFullscreenContext.Provider>
          </Rnd>
        </DialogPopup>
      </DialogPortal>
    </Dialog>
  );
}

export function ManagedDialogBody({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      data-slot="dialog-body"
      className={cn('min-h-0 flex-1 overflow-y-auto p-4', className)}
      {...props}
    />
  );
}

function ManagedDialogFooter({ children, switcher }: { children: ReactNode; switcher: ReactNode }) {
  const fullscreen = useContext(ManagedDialogFullscreenContext);
  return (
    <div
      data-slot="managed-dialog-footer"
      className={cn(
        'flex shrink-0 flex-col gap-2 border-t bg-muted/50 p-4 sm:flex-row sm:items-center',
        fullscreen && 'pb-20',
      )}
    >
      {switcher ? (
        <div data-slot="managed-dialog-footer-switcher" className="shrink-0 self-start">
          {switcher}
        </div>
      ) : null}
      <div
        data-slot="managed-dialog-footer-actions"
        className="flex flex-col-reverse gap-2 sm:ml-auto sm:flex-row sm:justify-end"
      >
        {children}
      </div>
    </div>
  );
}

function ManagedWindowMenu({
  label,
  compactLabel,
  entries,
  activeWindowId,
  contentAlign = 'end',
  onSelect,
}: {
  label: string;
  compactLabel?: string;
  entries: readonly (readonly [string, ManagedWindowEntry])[];
  activeWindowId: string | null;
  contentAlign?: 'start' | 'end';
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
            className={cn(
              'pointer-events-auto h-full shrink-0',
              managedDialogControlGeometry,
              opaquePopoverTriggerSurface,
            )}
          />
        }
      >
        {compactLabel ? null : <Layers3Icon />}
        <span>{compactLabel ?? label}</span>
      </DropdownMenuTrigger>
      <DropdownMenuContent side="top" align={contentAlign} className="w-72">
        <DropdownMenuGroup>
          <DropdownMenuLabel>{label}</DropdownMenuLabel>
          {entries.map(([candidateWindowId, candidateEntry]) => {
            const candidateActive = candidateWindowId === activeWindowId;
            return (
              <DropdownMenuItem
                key={candidateWindowId}
                aria-current={candidateActive ? 'true' : undefined}
                className={candidateActive ? persistentItemHighlight : undefined}
                onClick={() => onSelect(candidateWindowId, candidateEntry)}
              >
                <span className="min-w-0 flex-1 truncate">{candidateEntry.title}</span>
                {candidateEntry.dirty ? (
                  <span data-slot="managed-window-dirty-indicator" title={t('dialog.unsaved')}>
                    <span aria-hidden="true">•</span>
                    <span className="sr-only">{t('dialog.unsaved')}</span>
                  </span>
                ) : null}
                <span className="text-xs text-muted-foreground">
                  {candidateEntry.mode === 'docked' ? t('dialog.minimized') : t('dialog.expanded')}
                </span>
              </DropdownMenuItem>
            );
          })}
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

export {
  ManagedDialogAction,
  ManagedDialogAsyncAction,
  ManagedDialogIconAction,
  ManagedWindowMenu,
  managedDialogControlGeometry,
};

function readWorkArea(container: HTMLElement | null): WorkArea {
  if (container) {
    const rect = container.getBoundingClientRect();
    return {
      width: container.clientWidth || rect.width || window.innerWidth,
      height: container.clientHeight || rect.height || window.innerHeight,
    };
  }
  if (typeof window === 'undefined') return { width: 1024, height: 768 };
  return {
    width: window.visualViewport?.width ?? window.innerWidth,
    height: window.visualViewport?.height ?? window.innerHeight,
  };
}

function rectForPreset(
  workArea: WorkArea,
  preset: Exclude<ManagedWindowPreset, 'custom'>,
): ManagedWindowRect {
  return preset === 'fullscreen' ? fullscreenRect(workArea) : centeredWindowedRect(workArea);
}

function minimumSize(workArea: WorkArea) {
  return {
    width: workArea.width * MINIMUM_DIALOG_WIDTH_SCALE,
    height: workArea.height * MINIMUM_DIALOG_HEIGHT_SCALE,
  };
}

function windowedSize(workArea: WorkArea) {
  return {
    width: workArea.width * WINDOWED_DIALOG_WIDTH_SCALE,
    height: workArea.height * WINDOWED_DIALOG_HEIGHT_SCALE,
  };
}

function centeredWindowedRect(workArea: WorkArea): ManagedWindowRect {
  const size = windowedSize(workArea);
  return {
    ...size,
    x: (workArea.width - size.width) / 2,
    y: (workArea.height - size.height) / 2,
  };
}

function fullscreenRect(workArea: WorkArea): ManagedWindowRect {
  return { width: workArea.width, height: workArea.height, x: 0, y: 0 };
}

function clampRect(rect: ManagedWindowRect, workArea: WorkArea): ManagedWindowRect {
  const minimum = minimumSize(workArea);
  return clampPosition(
    {
      ...rect,
      width: Math.min(workArea.width, Math.max(minimum.width, rect.width)),
      height: Math.min(workArea.height, Math.max(minimum.height, rect.height)),
    },
    workArea,
  );
}

function clampPosition(rect: ManagedWindowRect, workArea: WorkArea): ManagedWindowRect {
  return {
    ...rect,
    x: Math.min(Math.max(0, rect.x), Math.max(0, workArea.width - rect.width)),
    y: Math.min(Math.max(0, rect.y), Math.max(0, workArea.height - rect.height)),
  };
}

function isWindowedSize(rect: ManagedWindowRect, workArea: WorkArea) {
  const windowed = windowedSize(workArea);
  return rect.width === windowed.width && rect.height === windowed.height;
}

function isFullscreen(rect: ManagedWindowRect, workArea: WorkArea) {
  return (
    rect.width === workArea.width && rect.height === workArea.height && rect.x === 0 && rect.y === 0
  );
}

function findWindowElement(slot: string, windowId: string) {
  return [...document.querySelectorAll<HTMLElement>(`[data-slot="${slot}"]`)].find(
    (element) => element.dataset.windowId === windowId,
  );
}

function trapFocus(event: ReactKeyboardEvent<HTMLDivElement>) {
  if (event.key !== 'Tab') return;
  const focusable = focusableElements(event.currentTarget);
  if (focusable.length === 0) {
    event.preventDefault();
    return;
  }
  const current = event.target instanceof HTMLElement ? event.target : document.activeElement;
  const currentIndex = focusable.indexOf(current as HTMLElement);
  if (event.shiftKey && currentIndex === 0) {
    event.preventDefault();
    focusable.at(-1)?.focus();
  } else if (!event.shiftKey && currentIndex === focusable.length - 1) {
    event.preventDefault();
    focusable[0]?.focus();
  }
}

function firstFocusable(container: HTMLElement) {
  return focusableElements(container)[0];
}

function focusableElements(container: HTMLElement) {
  return [
    ...container.querySelectorAll<HTMLElement>(
      'button:not(:disabled), a[href], input:not(:disabled), textarea:not(:disabled), select:not(:disabled), [tabindex]:not([tabindex="-1"])',
    ),
  ].filter((element) => {
    if (
      element.closest('[hidden], [aria-hidden="true"], [inert]') ||
      element.hasAttribute('hidden') ||
      element.getAttribute('aria-hidden') === 'true'
    ) {
      return false;
    }
    for (let current: HTMLElement | null = element; current; current = current.parentElement) {
      const style = window.getComputedStyle(current);
      if (style.display === 'none' || style.visibility === 'hidden') return false;
      if (current === container) break;
    }
    return true;
  });
}
