import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/landing/landing').then((m) => m.Landing),
  },
  {
    // Protected area — the guard + the SSR Express gate keep anonymous users out.
    path: 'app',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    // Account management. Same gate as /app — it is part of the protected area.
    path: 'app/account',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/account/account').then((m) => m.Account),
  },
  {
    // Without a scene, the editor opens the story's first one.
    path: 'app/stories/:storyId',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/scene-editor/scene-editor').then((m) => m.SceneEditor),
  },
  {
    path: 'app/stories/:storyId/scenes/:sceneId',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/scene-editor/scene-editor').then((m) => m.SceneEditor),
  },
  {
    // Catch-all: unmatched paths render a branded 404 (SSR returns HTTP 404) instead of
    // falling through to Express's bare "Cannot GET …".
    path: '**',
    loadComponent: () => import('./pages/not-found/not-found').then((m) => m.NotFound),
  },
];
