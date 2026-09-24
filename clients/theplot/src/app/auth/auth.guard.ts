import { inject, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { CanActivateFn } from '@angular/router';
import { AuthIdentityService } from './auth-identity';

/**
 * Defence-in-depth for client-side navigation into /app. Server-side, the SSR Express gate already
 * redirects anonymous users (no flash of protected chrome); this handles in-app SPA route changes.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthIdentityService);
  if (auth.authenticated) {
    return true;
  }

  if (isPlatformBrowser(inject(PLATFORM_ID))) {
    // /signin is a BFF endpoint, not an Angular route — full-page navigation.
    window.location.href = `/signin?returnUrl=${encodeURIComponent(state.url)}`;
  }

  return false;
};
