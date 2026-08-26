import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { subscriptionGuard } from './subscription/subscription.guard';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/landing/landing').then((m) => m.Landing),
  },
  {
    // The subscription workflow, which the auth callback diverts unsubscribed accounts
    // into. Deliberately NOT behind subscriptionGuard — it is the place that guard sends
    // people, and guarding it is a redirect loop.
    path: 'subscribe',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/subscribe/subscribe').then((m) => m.Subscribe),
  },
  {
    // Protected area — the guard + the SSR Express gate keep anonymous users out (guide §7).
    path: 'app',
    canActivate: [authGuard, subscriptionGuard],
    loadComponent: () => import('./pages/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    // Account management. Same gates as /app — it is part of the protected area — and the
    // subscription guard is exempt here (see SUBSCRIPTION_EXEMPT_PREFIXES): an account that
    // has not subscribed must still be able to leave.
    path: 'app/account',
    canActivate: [authGuard, subscriptionGuard],
    loadComponent: () => import('./pages/account/account').then((m) => m.Account),
  },
  {
    // Catch-all: unmatched paths render a branded 404 (SSR returns HTTP 404) instead of
    // falling through to Express's bare "Cannot GET …".
    path: '**',
    loadComponent: () => import('./pages/not-found/not-found').then((m) => m.NotFound),
  },
];
