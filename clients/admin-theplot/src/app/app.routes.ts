import { Routes } from '@angular/router';
import { consoleGuard, FORBIDDEN_ROUTE } from '../admin-kit';

export const routes: Routes = [
  FORBIDDEN_ROUTE,
  {
    path: '',
    canActivateChild: [consoleGuard],
    children: [
      {
        path: '',
        loadComponent: () => import('./pages/overview/overview').then((m) => m.Overview),
      },
      {
        path: 'stories',
        loadComponent: () => import('./pages/stories/stories').then((m) => m.Stories),
      },
      {
        path: 'stories/:id',
        loadComponent: () => import('./pages/story/story').then((m) => m.Story),
      },
      {
        path: '**',
        loadComponent: () => import('../admin-kit/pages/not-found').then((m) => m.NotFound),
      },
    ],
  },
];
