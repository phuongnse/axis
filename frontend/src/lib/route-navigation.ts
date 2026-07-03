export type PublicRouteEscapeTarget = '/sign-in' | '/register' | '/';

export interface PublicRouteNavigation {
  escapeTargets: readonly [PublicRouteEscapeTarget, ...PublicRouteEscapeTarget[]];
}

export function publicRouteNavigation(navigation: PublicRouteNavigation): PublicRouteNavigation {
  return navigation;
}
