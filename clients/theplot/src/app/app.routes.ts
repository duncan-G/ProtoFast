import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';

/**
 * ThePlot's whole product lives under /app, which is why there is no subscription guard here:
 * unlike the protofast client there is no unsubscribed state to divert to. The SSR Express gate
 * in server.ts handles the anonymous case server-side; authGuard covers in-app navigation.
 */
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/landing/landing').then((m) => m.Landing),
  },
  {
    path: 'app',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/library/library').then((m) => m.Library),
  },
  {
    path: 'app/upload',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/upload/upload').then((m) => m.Upload),
  },
  {
    path: 'app/runs/:runId',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/run/run').then((m) => m.RunPage),
  },
  {
    path: 'app/runs/:runId/tree',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/tree/tree').then((m) => m.TreePage),
  },
  {
    // The scene stream (scene plan §2): the same frozen record as the tree, read as what the
    // document is rather than as how it is filed.
    path: 'app/runs/:runId/scenes',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/scenes/scenes').then((m) => m.ScenesPage),
  },
  {
    path: 'app/runs/:runId/augmentations',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./pages/augmentations/augmentations').then((m) => m.AugmentationsPage),
  },
  {
    // The freeze gate (plan §9.10). Gated on the segmentation-reviewer realm role rather than a
    // separate client, so a reviewer signs in to ThePlot like any other user.
    path: 'app/reviews',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/reviews/reviews').then((m) => m.ReviewsPage),
  },
  {
    path: 'app/reviews/:reviewId',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/reviews/review-detail').then((m) => m.ReviewDetailPage),
  },
  {
    path: 'app/account',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/account/account').then((m) => m.Account),
  },
  {
    // Catch-all: unmatched paths render a branded 404 (SSR returns HTTP 404) instead of falling
    // through to Express's bare "Cannot GET …".
    path: '**',
    loadComponent: () => import('./pages/not-found/not-found').then((m) => m.NotFound),
  },
];
