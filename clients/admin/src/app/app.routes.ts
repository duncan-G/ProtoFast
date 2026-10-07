import { Routes } from '@angular/router';
import { consoleGuard, FORBIDDEN_ROUTE, roleGuard } from '../admin-kit';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'app' },
  FORBIDDEN_ROUTE,
  {
    path: 'app',
    canActivate: [consoleGuard],
    loadComponent: () => import('./pages/consoles/consoles').then((m) => m.Consoles),
  },
  {
    path: 'app/account',
    canActivate: [consoleGuard],
    loadComponent: () => import('./pages/account/account').then((m) => m.Account),
  },
  {
    path: 'app/platform',
    canActivate: [roleGuard('platform')],
    loadComponent: () => import('./pages/platform/platform').then((m) => m.Platform),
  },
  {
    path: '**',
    loadComponent: () => import('../admin-kit/pages/not-found').then((m) => m.NotFound),
  },
];
