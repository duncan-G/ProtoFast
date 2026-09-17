import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AccountMenu } from './account-menu';
import { ThePlotLogo } from './theplot-logo';

/**
 * The signed-in chrome every /app page sits inside: the wordmark, the three places a user goes,
 * and the account menu. One component rather than a header copied into each page, so the nav
 * cannot drift between screens.
 */
@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AccountMenu, RouterLink, RouterLinkActive, ThePlotLogo],
  template: `
    <div class="nocturne min-h-screen bg-[var(--color-bg)] text-[var(--color-text)] antialiased">
      <header
        class="sticky top-0 z-50 border-b border-[var(--color-divider)] bg-[var(--color-bg)]/85 backdrop-blur"
      >
        <nav class="mx-auto flex max-w-7xl items-center justify-between gap-4 px-6 py-3">
          <a routerLink="/app" class="shrink-0 text-base">
            <app-theplot-logo />
          </a>

          <div class="flex min-w-0 items-center gap-1 text-sm">
            <a routerLink="/app" routerLinkActive="nav-active" [routerLinkActiveOptions]="{ exact: true }" class="nav-link">
              Library
            </a>
            <a routerLink="/app/upload" routerLinkActive="nav-active" class="nav-link">Upload</a>
            @if (showReviews()) {
              <a routerLink="/app/reviews" routerLinkActive="nav-active" class="nav-link">Reviews</a>
            }
          </div>

          <app-account-menu />
        </nav>
      </header>

      <main class="mx-auto max-w-7xl px-6 py-10">
        <ng-content />
      </main>
    </div>
  `,
  styles: `
    .nav-link {
      border-radius: var(--radius-md);
      padding: 0.375rem 0.75rem;
      color: var(--color-neutral-400);
      transition: color 120ms ease, background-color 120ms ease;
    }
    .nav-link:hover {
      color: var(--color-text);
      background-color: var(--color-surface);
    }
    .nav-active {
      color: var(--color-accent-300);
      background-color: var(--color-surface);
    }
  `,
})
export class AppShell {
  /** The reviews queue is only meaningful to someone who can act on it. */
  readonly showReviews = input(false);
}
