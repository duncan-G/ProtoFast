import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AccountMenu } from '../../shared/account-menu';
import { AuthIdentityService } from '../../auth/auth-identity';
import { TheplotLogo } from '../../shared/theplot-logo';
import {
  LANDING_MANUSCRIPT,
  LANDING_STEPS,
  LANDING_STORIES,
} from './landing.data';

/**
 * The marketing page — the only page an anonymous visitor sees. Layout and
 * copy follow the Claude Design project "ThePlot app design system"; the
 * design's screen switches become real navigations here ( /signin, /signup
 * are BFF endpoints, full-page navigations).
 *
 * The header is auth-aware: signed out it offers Sign in / Start free,
 * signed in it collapses to "Open ThePlot" and the account menu, because the
 * person most likely to land here twice is the one who already has an account.
 */
@Component({
  selector: 'app-landing',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AccountMenu, TheplotLogo],
  templateUrl: './landing.html',
  // Escape closes the menu panel from anywhere, including while focus sits on
  // one of its links.
  host: { '(document:keydown.escape)': 'closeMenu()' },
})
export class Landing {
  protected readonly auth = inject(AuthIdentityService);

  /**
   * Whether the collapsed-nav menu panel is open. Below sm the section links
   * and Sign in live in it — on a phone it is the only route to the account
   * actions, so nothing else may gate it.
   */
  protected readonly menuOpen = signal(false);

  protected readonly steps = LANDING_STEPS;
  protected readonly stories = LANDING_STORIES;
  protected readonly manuscript = LANDING_MANUSCRIPT;
  protected readonly year = new Date().getFullYear();

  protected toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  protected closeMenu(): void {
    this.menuOpen.set(false);
  }
}
