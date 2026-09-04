import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { ImagePlaceholder } from './image-placeholder';
import { ProtofastLogo } from '../../shared/protofast-logo';
import { AccountMenu } from '../../shared/account-menu';
import { AuthIdentityService } from '../../auth/auth-identity';
import {
  LANDING_AGENT_FEED,
  LANDING_FEATURES,
  LANDING_MILESTONES,
  LANDING_PRICING_TIERS,
  LANDING_PROMPTS,
  LANDING_STATS,
  LANDING_STEPS,
} from './landing.data';

@Component({
  selector: 'app-landing',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AccountMenu, ImagePlaceholder, ProtofastLogo, RouterLink],
  templateUrl: './landing.html',
  // Escape closes the menu panel from anywhere, including while focus sits on
  // one of its links.
  host: { '(document:keydown.escape)': 'closeMenu()' },
})
export class Landing {
  protected readonly auth = inject(AuthIdentityService);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * Whether the collapsed-nav menu panel is open. Below lg the section links
   * live in it, and below sm so do Sign in / Start building — on a phone it is
   * the only route to the account actions, so nothing else may gate it.
   */
  protected readonly menuOpen = signal(false);

  protected toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  protected closeMenu(): void {
    this.menuOpen.set(false);
  }

  private readonly prompts = LANDING_PROMPTS;

  /**
   * The hero prompt text. Seeded with the first prompt in full so SSR renders a
   * complete sentence — crawlers see real copy and there is no layout shift when
   * the browser takes over. The animation only ever runs client-side.
   */
  protected readonly typed = signal(this.prompts[0]);

  protected readonly agentFeed = LANDING_AGENT_FEED;
  protected readonly stats = LANDING_STATS;
  protected readonly steps = LANDING_STEPS;
  protected readonly features = LANDING_FEATURES;
  protected readonly milestones = LANDING_MILESTONES;
  protected readonly pricingTiers = LANDING_PRICING_TIERS;

  constructor() {
    afterNextRender(() => this.startTypewriter());
  }

  /**
   * Cycles the hero field through `prompts`: hold the finished sentence, delete it,
   * type the next one. Browser-only (see `typed`), and a no-op for anyone who has
   * asked for reduced motion — they keep the seeded sentence, which is already
   * complete and readable.
   */
  private startTypewriter(): void {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      return;
    }

    const HOLD_MS = 2400;
    const TYPE_MS = 38;
    const DELETE_MS = 16;

    let promptIndex = 0;
    let chars = this.prompts[0].length;
    let phase: 'holding' | 'deleting' | 'typing' = 'holding';

    // Advances one frame of the animation and reports how long to wait for the next.
    const advance = (): number => {
      switch (phase) {
        case 'holding':
          phase = 'deleting';
          return DELETE_MS;
        case 'deleting':
          chars -= 1;
          if (chars <= 0) {
            promptIndex = (promptIndex + 1) % this.prompts.length;
            phase = 'typing';
          }
          return DELETE_MS;
        case 'typing':
          chars += 1;
          if (chars >= this.prompts[promptIndex].length) {
            phase = 'holding';
            return HOLD_MS;
          }
          return TYPE_MS;
      }
    };

    const run = () => {
      const delay = advance();
      this.typed.set(this.prompts[promptIndex].slice(0, chars));
      timer = setTimeout(run, delay);
    };

    let timer = setTimeout(run, HOLD_MS);
    this.destroyRef.onDestroy(() => clearTimeout(timer));
  }
}
