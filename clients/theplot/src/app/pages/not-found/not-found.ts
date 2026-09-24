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
    <div class="ember flex min-h-screen flex-col items-center justify-center px-6 text-center antialiased">
      <p
        class="m-0 font-[family-name:var(--font-mono)] text-[12px] tracking-[0.2em] text-[var(--color-accent-bright)] uppercase"
      >
        404
      </p>
      <h1 class="mt-4 text-[clamp(40px,6vw,64px)]">This page lost <span class="italic">the plot.</span></h1>
      <p class="mt-2 max-w-md text-[var(--color-neutral-400)]">
        It doesn't exist, or it may have moved between chapters.
      </p>
      <a routerLink="/" class="btn btn-primary mt-8">Back to the top</a>
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
