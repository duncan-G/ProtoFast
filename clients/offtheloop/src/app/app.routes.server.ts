import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  {
    // Server-rendered per request so SSR can read the ext_authz identity headers and render
    // personalized-or-anonymous HTML (guide §7). Edge caching is governed by Cache-Control.
    path: '**',
    renderMode: RenderMode.Server,
  },
];
