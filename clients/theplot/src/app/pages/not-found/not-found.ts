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
 * Catch-all 404. Reached via the `**` route so unmatched paths render branded chrome instead of
 * Express's bare "Cannot GET …". During SSR it also stamps the outgoing response with a real 404
 * via `RESPONSE_INIT`, so crawlers and the edge see the correct code.
 */
@Component({
  selector: 'app-not-found',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div
      class="nocturne flex min-h-screen flex-col items-center justify-center bg-[var(--color-bg)] px-6 text-center text-[var(--color-text)] antialiased"
    >
      <p class="text-sm font-semibold tracking-widest text-[var(--color-accent-400)] uppercase">404</p>
      <h1 class="mt-4 text-4xl font-semibold tracking-tight sm:text-5xl">Page not found</h1>
      <p class="mt-4 max-w-md text-[var(--color-neutral-400)]">
        The page you are looking for doesn’t exist or may have moved.
      </p>
      <a routerLink="/app" class="btn btn-primary mt-8">Back to your library</a>
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
