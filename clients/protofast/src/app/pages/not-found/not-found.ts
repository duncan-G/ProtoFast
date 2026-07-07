import {
  ChangeDetectionStrategy,
  Component,
  inject,
  PLATFORM_ID,
  RESPONSE_INIT,
} from '@angular/core';
import { isPlatformServer } from '@angular/common';
import { RouterLink } from '@angular/router';

/**
 * Catch-all 404 page. Reached via the `**` route so unmatched paths render branded chrome
 * instead of Express's bare "Cannot GET …". During SSR it also stamps the outgoing response
 * with a real 404 status via `RESPONSE_INIT` so crawlers and the edge see the correct code.
 */
@Component({
  selector: 'app-not-found',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div
      class="flex min-h-screen flex-col items-center justify-center bg-slate-950 px-6 text-center text-slate-100 antialiased"
    >
      <p class="text-sm font-semibold uppercase tracking-widest text-amber-400">404</p>
      <h1 class="mt-4 text-4xl font-bold tracking-tight sm:text-5xl">Page not found</h1>
      <p class="mt-4 max-w-md text-slate-400">
        The page you are looking for doesn’t exist or may have moved.
      </p>
      <a
        routerLink="/"
        class="mt-8 rounded-lg bg-gradient-to-br from-amber-300 to-orange-500 px-5 py-2.5 text-sm font-semibold text-slate-950 transition hover:opacity-90"
      >
        Back to home
      </a>
    </div>
  `,
})
export class NotFound {
  private readonly platformId = inject(PLATFORM_ID);
  private readonly responseInit = inject(RESPONSE_INIT, { optional: true });

  constructor() {
    if (isPlatformServer(this.platformId) && this.responseInit) {
      this.responseInit.status = 404;
    }
  }
}
