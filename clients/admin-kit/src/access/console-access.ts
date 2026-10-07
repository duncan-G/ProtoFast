import { inject, InjectionToken, makeStateKey, PLATFORM_ID, TransferState } from '@angular/core';
import { isPlatformBrowser, PlatformLocation } from '@angular/common';
import { CanActivateFn, Route, Router, UrlTree } from '@angular/router';
import { Forbidden } from '../pages/forbidden';
import { AuthIdentityService } from './auth-identity';

/** Any one of these roles opens the console; `*` admits any signed-in operator. */
export const CONSOLE_ROLES_KEY = makeStateKey<string[]>('adminKit.consoleRoles');

/** Set by the deployment (ADMIN_CONSOLE_ROLES) and transferred from SSR, never by the console. */
export const CONSOLE_ROLES = new InjectionToken<string[]>('CONSOLE_ROLES', {
  providedIn: 'root',
  factory: () => inject(TransferState).get(CONSOLE_ROLES_KEY, []),
});

export const SIGN_IN_PATH = '/signin';
export const FORBIDDEN_PATH = '/forbidden';

export function admits(required: readonly string[], held: readonly string[]): boolean {
  return required.includes('*') || required.some((role) => held.includes(role));
}

/** Client-side navigation into the console; the SSR gate covers the first request. */
export const consoleGuard: CanActivateFn = (_route, state) =>
  gate(inject(CONSOLE_ROLES), state.url);

export function roleGuard(role: string): CanActivateFn {
  return (_route, state) => gate([role], state.url);
}

/** Every console declares it, so the guards can send an operator there during SSR too. */
export const FORBIDDEN_ROUTE: Route = {
  path: 'forbidden',
  component: Forbidden,
};

function gate(required: readonly string[], url: string): boolean | UrlTree {
  const auth = inject(AuthIdentityService);
  if (auth.authenticated) {
    return admits(required, auth.identity.roles) || inject(Router).parseUrl(FORBIDDEN_PATH);
  }

  if (isPlatformBrowser(inject(PLATFORM_ID))) {
    const base = inject(PlatformLocation).getBaseHrefFromDOM().replace(/\/$/, '');
    window.location.href = `${SIGN_IN_PATH}?returnUrl=${encodeURIComponent(base + url)}`;
  }

  return false;
}
