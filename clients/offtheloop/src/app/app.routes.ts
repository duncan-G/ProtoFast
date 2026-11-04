import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';

export const routes: Routes = [
  {
    path: '',
    title: 'offtheloop · job-search tracker for engineers',
    loadComponent: () => import('./pages/landing/landing').then((m) => m.Landing),
  },
  {
    // The SSR gate in server.ts keeps anonymous requests out too.
    path: 'app',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/shell/shell').then((m) => m.Shell),
    children: [
      {
        path: '',
        title: 'Today · offtheloop',
        loadComponent: () => import('./pages/today/today').then((m) => m.Today),
      },
      {
        path: 'notes',
        title: 'Notes · offtheloop',
        loadComponent: () => import('./pages/notes/notes').then((m) => m.Notes),
      },
      {
        path: 'loop',
        title: 'The loop · offtheloop',
        loadComponent: () => import('./pages/loop/loop').then((m) => m.Loop),
      },
      {
        path: 'account',
        title: 'Account · offtheloop',
        loadComponent: () => import('./pages/account/account').then((m) => m.Account),
      },
    ],
  },
  {
    path: '**',
    title: 'Not found · offtheloop',
    loadComponent: () => import('./pages/not-found/not-found').then((m) => m.NotFound),
  },
];
