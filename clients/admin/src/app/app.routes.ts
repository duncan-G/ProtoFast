import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/home/home').then((m) => m.Home),
  },
  {
    // Protected area — the guard + the SSR Express gate keep anonymous users out (guide §7).
    path: 'app',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    // Catch-all: unmatched paths render a 404 page (SSR returns HTTP 404) instead of
    // falling through to Express's bare "Cannot GET …".
    path: '**',
    loadComponent: () => import('./pages/not-found/not-found').then((m) => m.NotFound),
  },
];
