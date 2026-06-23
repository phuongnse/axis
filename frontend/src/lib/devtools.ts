declare global {
  interface Window {
    __AXIS_DISABLE_DEVTOOLS__?: boolean;
  }
}

export function shouldRenderDevtools() {
  return (
    import.meta.env.DEV &&
    typeof window !== 'undefined' &&
    window.__AXIS_DISABLE_DEVTOOLS__ !== true
  );
}
