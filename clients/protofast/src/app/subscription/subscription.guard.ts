import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import {
  hasSubscribeFlag,
  isSubscriptionExempt,
  SUBSCRIBE_ROUTE,
} from './subscription-flag';

/**
 * Sends a sign-in the callback flagged as unsubscribed into the checkout workflow instead
 * of the dashboard.
 *
 * The flag arrives once, on the return URL, and is not persisted: the workflow owns the
 * user from there, and an account that finishes checkout simply stops being flagged on the
 * next sign-in. Anything already inside the workflow — or on the way out of the account —
 * is exempt, because a guard that redirects the page it redirects *to* is a loop.
 */
export const subscriptionGuard: CanActivateFn = (route, state) => {
  if (isSubscriptionExempt(state.url) || !hasSubscribeFlag(route, state)) {
    return true;
  }

  return inject(Router).createUrlTree([SUBSCRIBE_ROUTE], {
    queryParams: { returnUrl: state.url.split('?')[0] },
  });
};
