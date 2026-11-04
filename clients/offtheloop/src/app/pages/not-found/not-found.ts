import {
  ChangeDetectionStrategy,
  Component,
  inject,
  PLATFORM_ID,
  RESPONSE_INIT,
} from '@angular/core';
import { isPlatformServer } from '@angular/common';
import { RouterLink } from '@angular/router';

/** The `**` route; during SSR it answers with a real 404. */
@Component({
  selector: 'app-not-found',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="flex min-h-screen flex-col items-center justify-center gap-4 px-6 text-center">
      <p class="mono m-0 text-[13px] text-[var(--color-dim)]">404</p>
      <h1 class="display">
        This page is <span class="text-[var(--color-accent)]">off the loop.</span>
      </h1>
      <p class="m-0 text-[var(--color-muted)]">It doesn’t exist, or it moved.</p>
      <a routerLink="/" class="btn btn-primary mt-4">Back to the start</a>
    </div>
  `,
})
export class NotFound {
  constructor() {
    const responseInit = inject(RESPONSE_INIT, { optional: true });
    if (isPlatformServer(inject(PLATFORM_ID)) && responseInit) {
      responseInit.status = 404;
    }
  }
}
