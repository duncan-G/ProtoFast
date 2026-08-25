import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

/**
 * Query flag the auth callback appends to the return URL for an account that has not
 * subscribed yet. The name is shared with the BFF
 * (`services/auth/src/ProtoFast.Auth.Api/Configuration/SubscriptionOptions.cs`); change
 * one and change the other.
 */
export const SUBSCRIBE_FLAG = 'subscribe';

/** Where a flagged sign-in is sent instead of the dashboard. */
export const SUBSCRIBE_ROUTE = '/subscribe';

/**
 * Routes that must stay reachable while the flag is set, or the redirect loops: the
 * subscription workflow itself, and the way out of the account.
 *
 * `/signout` is a BFF endpoint rather than an Angular route, so it never reaches the
 * guard — it is listed anyway because the allowlist is the thing anyone will read to
 * answer "what is exempt?", and a future in-app sign-out link should not have to
 * rediscover this.
 */
export const SUBSCRIPTION_EXEMPT_PREFIXES = [SUBSCRIBE_ROUTE, '/signout'];

export function isSubscriptionExempt(url: string): boolean {
  const path = url.split('?')[0];
  return SUBSCRIPTION_EXEMPT_PREFIXES.some(
    (prefix) => path === prefix || path.startsWith(`${prefix}/`),
  );
}

/** Was this navigation flagged by the callback as "this account still has to subscribe"? */
export function hasSubscribeFlag(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): boolean {
  return route.queryParamMap.get(SUBSCRIBE_FLAG) === '1' || state.url.includes(`${SUBSCRIBE_FLAG}=1`);
}
