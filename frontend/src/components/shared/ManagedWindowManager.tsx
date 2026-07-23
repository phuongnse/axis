import {
  type ComponentType,
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
} from 'react';
import { useStore } from 'zustand';
import { createStore, type StoreApi } from 'zustand/vanilla';

export type ManagedWindowPreset = 'large' | 'custom' | 'fullscreen';
export type ManagedWindowInitialSize = 'auto' | Exclude<ManagedWindowPreset, 'custom'>;
export type ManagedWindowAutoSizeStatus = 'pending' | 'resolved' | 'manual';

export interface ManagedWindowRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface ManagedWindowDescriptor {
  id: string;
  kind: string;
  resourceKey: string;
  title: string;
  payload?: unknown;
  initialSize?: ManagedWindowInitialSize;
}

export interface ManagedWindowRendererProps {
  descriptor: ManagedWindowDescriptor;
}

export type ManagedWindowRendererRegistry = Readonly<
  Record<string, ComponentType<ManagedWindowRendererProps>>
>;

export interface ManagedWindowEntry {
  descriptor: ManagedWindowDescriptor;
  mode: 'expanded' | 'docked';
  preset: ManagedWindowPreset;
  rect: ManagedWindowRect | null;
  maximizeSnapshot: {
    preset: ManagedWindowPreset;
    rect: ManagedWindowRect;
  } | null;
  autoSize: {
    key: string | null;
    status: ManagedWindowAutoSizeStatus;
  } | null;
  zIndex: number;
  dockOrder: number;
  title: string;
  dirty: boolean;
  busy: boolean;
}

interface ManagedWindowStoreState {
  windows: Record<string, ManagedWindowEntry>;
  windowOrder: string[];
  activeWindowId: string | null;
  zCounter: number;
  dockCounter: number;
  openWindow: (descriptor: ManagedWindowDescriptor) => void;
  focusWindow: (windowId: string) => void;
  minimizeWindow: (windowId: string) => void;
  restoreWindow: (windowId: string) => void;
  replaceWindow: (windowId: string, descriptor: ManagedWindowDescriptor) => void;
  closeWindow: (windowId: string) => void;
  clearWindows: () => void;
  updateWindow: (windowId: string, patch: Partial<ManagedWindowEntry>) => void;
  updateRuntime: (
    windowId: string,
    runtime: Pick<ManagedWindowEntry, 'title' | 'dirty' | 'busy'>,
  ) => void;
}

type CloseRequestHandler = () => void;

interface ManagedWindowManagerContextValue {
  store: StoreApi<ManagedWindowStoreState>;
  renderers: ManagedWindowRendererRegistry;
  portalContainer: HTMLElement | null;
  setPortalContainer: (container: HTMLElement | null) => void;
  registerCloseRequest: (windowId: string, handler: CloseRequestHandler | null) => void;
  requestClose: (windowId: string) => void;
}

const ManagedWindowManagerContext = createContext<ManagedWindowManagerContextValue | null>(null);
const CurrentManagedWindowContext = createContext<string | null>(null);

export function ManagedWindowProvider({
  children,
  renderers,
}: {
  children: ReactNode;
  renderers: ManagedWindowRendererRegistry;
}) {
  const storeRef = useRef<StoreApi<ManagedWindowStoreState> | null>(null);
  if (!storeRef.current) storeRef.current = createManagedWindowStore();
  const store = storeRef.current;
  const closeRequestsRef = useRef(new Map<string, CloseRequestHandler>());
  const [portalContainer, setPortalContainer] = useState<HTMLElement | null>(null);

  const registerCloseRequest = useCallback(
    (windowId: string, handler: CloseRequestHandler | null) => {
      if (handler) closeRequestsRef.current.set(windowId, handler);
      else closeRequestsRef.current.delete(windowId);
    },
    [],
  );

  const requestClose = useCallback(
    (windowId: string) => {
      const entry = store.getState().windows[windowId];
      if (!entry || entry.busy) return;
      const handler = closeRequestsRef.current.get(windowId);
      if (handler) handler();
      else store.getState().closeWindow(windowId);
    },
    [store],
  );

  const value = useMemo<ManagedWindowManagerContextValue>(
    () => ({
      store,
      renderers,
      portalContainer,
      setPortalContainer,
      registerCloseRequest,
      requestClose,
    }),
    [portalContainer, registerCloseRequest, renderers, requestClose, store],
  );

  return (
    <ManagedWindowManagerContext.Provider value={value}>
      {children}
    </ManagedWindowManagerContext.Provider>
  );
}

export function ManagedWindowRendererScope({
  windowId,
  children,
}: {
  windowId: string;
  children: ReactNode;
}) {
  return (
    <CurrentManagedWindowContext.Provider value={windowId}>
      {children}
    </CurrentManagedWindowContext.Provider>
  );
}

export function useManagedWindowStore<T>(selector: (state: ManagedWindowStoreState) => T): T {
  const manager = useManagedWindowManagerContext();
  return useStore(manager.store, selector);
}

export function useManagedWindowActions() {
  const manager = useManagedWindowManagerContext();
  const openWindow = useStore(manager.store, (state) => state.openWindow);
  const focusWindow = useStore(manager.store, (state) => state.focusWindow);
  const minimizeWindow = useStore(manager.store, (state) => state.minimizeWindow);
  const restoreWindow = useStore(manager.store, (state) => state.restoreWindow);
  const replaceWindow = useStore(manager.store, (state) => state.replaceWindow);
  const closeWindow = useStore(manager.store, (state) => state.closeWindow);
  const clearWindows = useStore(manager.store, (state) => state.clearWindows);

  return {
    openWindow,
    focusWindow,
    minimizeWindow,
    restoreWindow,
    replaceWindow,
    closeWindow,
    clearWindows,
    requestClose: manager.requestClose,
  };
}

export function useCurrentManagedWindow() {
  const windowId = useContext(CurrentManagedWindowContext);
  if (!windowId) throw new Error('Managed window renderer must be hosted by ManagedWindowHost.');
  const entry = useManagedWindowStore((state) => state.windows[windowId]);
  const actions = useManagedWindowActions();
  return { windowId, entry, ...actions };
}

export function useManagedWindowRuntime() {
  const windowId = useContext(CurrentManagedWindowContext);
  if (!windowId) throw new Error('ManagedDialog must render inside a managed window renderer.');
  const manager = useManagedWindowManagerContext();
  const entry = useStore(manager.store, (state) => state.windows[windowId]);
  const activeWindowId = useStore(manager.store, (state) => state.activeWindowId);
  const updateWindow = useStore(manager.store, (state) => state.updateWindow);
  const updateRuntime = useStore(manager.store, (state) => state.updateRuntime);
  const focusWindow = useStore(manager.store, (state) => state.focusWindow);
  const minimizeWindow = useStore(manager.store, (state) => state.minimizeWindow);

  return {
    windowId,
    entry,
    active: activeWindowId === windowId,
    portalContainer: manager.portalContainer,
    updateWindow,
    updateRuntime,
    focusWindow,
    minimizeWindow,
    registerCloseRequest: manager.registerCloseRequest,
    requestClose: manager.requestClose,
  };
}

export function useManagedWindowHostContext() {
  return useManagedWindowManagerContext();
}

function useManagedWindowManagerContext() {
  const value = useContext(ManagedWindowManagerContext);
  if (!value) throw new Error('Managed window APIs require ManagedWindowProvider.');
  return value;
}

function createManagedWindowStore(): StoreApi<ManagedWindowStoreState> {
  return createStore<ManagedWindowStoreState>((set) => ({
    windows: {},
    windowOrder: [],
    activeWindowId: null,
    zCounter: 0,
    dockCounter: 0,

    openWindow: (descriptor) =>
      set((state) => {
        const nextZ = nextZState(state);
        const existing = nextZ.windows[descriptor.id];
        const zCounter = nextZ.zCounter;
        if (existing) {
          return {
            windows: {
              ...nextZ.windows,
              [descriptor.id]: {
                ...existing,
                descriptor,
                title: existing.title || descriptor.title,
                mode: 'expanded',
                zIndex: zCounter,
              },
            },
            activeWindowId: descriptor.id,
            zCounter,
          };
        }

        return {
          windows: {
            ...nextZ.windows,
            [descriptor.id]: {
              descriptor,
              mode: 'expanded',
              preset: initialPreset(descriptor.initialSize),
              rect: null,
              maximizeSnapshot: null,
              autoSize:
                (descriptor.initialSize ?? 'auto') === 'auto'
                  ? { key: null, status: 'pending' }
                  : null,
              zIndex: zCounter,
              dockOrder: 0,
              title: descriptor.title,
              dirty: false,
              busy: false,
            },
          },
          windowOrder: [...state.windowOrder, descriptor.id],
          activeWindowId: descriptor.id,
          zCounter,
        };
      }),

    focusWindow: (windowId) =>
      set((state) => {
        const entry = state.windows[windowId];
        if (entry?.mode !== 'expanded' || state.activeWindowId === windowId) return state;
        const nextZ = nextZState(state);
        const zCounter = nextZ.zCounter;
        return {
          windows: {
            ...nextZ.windows,
            [windowId]: { ...nextZ.windows[windowId], zIndex: zCounter },
          },
          activeWindowId: windowId,
          zCounter,
        };
      }),

    minimizeWindow: (windowId) =>
      set((state) => {
        const entry = state.windows[windowId];
        if (!entry || entry.mode === 'docked') return state;
        const dockCounter = state.dockCounter + 1;
        const windows = {
          ...state.windows,
          [windowId]: { ...entry, mode: 'docked' as const, dockOrder: dockCounter },
        };
        return {
          windows,
          dockCounter,
          activeWindowId:
            state.activeWindowId === windowId
              ? highestExpandedWindowId(windows, windowId)
              : state.activeWindowId,
        };
      }),

    restoreWindow: (windowId) =>
      set((state) => {
        const entry = state.windows[windowId];
        if (!entry) return state;
        const nextZ = nextZState(state);
        const zCounter = nextZ.zCounter;
        return {
          windows: {
            ...nextZ.windows,
            [windowId]: { ...nextZ.windows[windowId], mode: 'expanded', zIndex: zCounter },
          },
          activeWindowId: windowId,
          zCounter,
        };
      }),

    replaceWindow: (windowId, descriptor) =>
      set((state) => {
        const current = state.windows[windowId];
        if (!current) return state;
        const target = state.windows[descriptor.id];
        if (target && descriptor.id !== windowId) {
          const nextZ = nextZState(state);
          const withoutCurrent = { ...nextZ.windows };
          delete withoutCurrent[windowId];
          const zCounter = nextZ.zCounter;
          withoutCurrent[descriptor.id] = {
            ...target,
            descriptor,
            mode: 'expanded',
            zIndex: zCounter,
          };
          return {
            windows: withoutCurrent,
            windowOrder: state.windowOrder.filter((id) => id !== windowId),
            activeWindowId: descriptor.id,
            zCounter,
          };
        }

        if (descriptor.id === windowId) {
          return {
            windows: {
              ...state.windows,
              [windowId]: { ...current, descriptor, title: descriptor.title },
            },
          };
        }

        const windows = { ...state.windows };
        delete windows[windowId];
        windows[descriptor.id] = {
          ...current,
          descriptor,
          title: descriptor.title,
          dirty: false,
          busy: false,
        };
        return {
          windows,
          windowOrder: state.windowOrder.map((id) => (id === windowId ? descriptor.id : id)),
          activeWindowId: state.activeWindowId === windowId ? descriptor.id : state.activeWindowId,
        };
      }),

    closeWindow: (windowId) =>
      set((state) => {
        if (!state.windows[windowId]) return state;
        const windows = { ...state.windows };
        delete windows[windowId];
        return {
          windows,
          windowOrder: state.windowOrder.filter((id) => id !== windowId),
          activeWindowId:
            state.activeWindowId === windowId
              ? highestExpandedWindowId(windows, windowId)
              : state.activeWindowId,
        };
      }),

    clearWindows: () =>
      set({
        windows: {},
        windowOrder: [],
        activeWindowId: null,
        zCounter: 0,
        dockCounter: 0,
      }),

    updateWindow: (windowId, patch) =>
      set((state) => {
        const entry = state.windows[windowId];
        if (!entry) return state;
        return { windows: { ...state.windows, [windowId]: { ...entry, ...patch } } };
      }),

    updateRuntime: (windowId, runtime) =>
      set((state) => {
        const entry = state.windows[windowId];
        if (
          !entry ||
          (entry.title === runtime.title &&
            entry.dirty === runtime.dirty &&
            entry.busy === runtime.busy)
        )
          return state;
        return { windows: { ...state.windows, [windowId]: { ...entry, ...runtime } } };
      }),
  }));
}

function nextZState(state: Pick<ManagedWindowStoreState, 'windows' | 'zCounter'>): {
  windows: Record<string, ManagedWindowEntry>;
  zCounter: number;
} {
  if (state.zCounter < 10_000) {
    return { windows: state.windows, zCounter: state.zCounter + 1 };
  }
  const windows = { ...state.windows };
  const ordered = Object.entries(windows).sort((left, right) => left[1].zIndex - right[1].zIndex);
  ordered.forEach(([windowId, entry], index) => {
    windows[windowId] = { ...entry, zIndex: index + 1 };
  });
  return { windows, zCounter: ordered.length + 1 };
}

function highestExpandedWindowId(windows: Record<string, ManagedWindowEntry>, excludedId?: string) {
  return (
    Object.entries(windows)
      .filter(([id, entry]) => id !== excludedId && entry.mode === 'expanded')
      .sort((left, right) => right[1].zIndex - left[1].zIndex)[0]?.[0] ?? null
  );
}

function initialPreset(initialSize: ManagedWindowInitialSize | undefined): ManagedWindowPreset {
  return initialSize === 'fullscreen' ? 'fullscreen' : 'large';
}
