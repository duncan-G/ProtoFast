import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The ThePlot lockup, from the design system: Newsreader serif set on one
 * baseline — "The" italic at 400 against "Plot" at 500 — closed with the
 * plot-point, a small square of the brand accent. No mark besides the dot;
 * the wordmark IS the logo.
 *
 * The same lockup is drawn by the Keycloak theme's pfBrandLockup
 * (infra/keycloak/themes/theplot/login/template.ftl) — if one changes,
 * change both.
 *
 * Sizing is in `em`, so the lockup scales with whatever font-size the caller
 * sets on the host — the nav renders it at 26px, the footer at 18px, and the
 * proportions hold at both.
 */
@Component({
  selector: 'app-theplot-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-flex items-baseline' },
  template: `
    <span
      class="font-[family-name:var(--font-heading)] tracking-[-0.01em] whitespace-nowrap text-[var(--color-text)]"
    >
      <span class="italic font-normal">The</span><span class="font-medium">Plot</span></span
    ><span
      class="ml-[0.12em] inline-block h-[0.27em] w-[0.27em] bg-[var(--color-accent)]"
      aria-hidden="true"
    ></span>
  `,
})
export class TheplotLogo {
  /** Kept for API compatibility with tight headers; the wordmark is the mark. */
  readonly showWordmark = input(true);
}
