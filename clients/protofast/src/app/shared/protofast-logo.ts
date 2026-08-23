import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The Protofast lockup — mark plus wordmark.
 *
 * The mark is direction 2d "Swarm" from the Claude Design canvas "Protofast
 * Landing" (Protofast Landing.dc.html, option 2d): five parallel strokes of
 * decreasing weight resolving into one solid dot — parallel agents converging
 * on a single shipped app. It is the mark that matches the "Console" page
 * direction, where the swarm is the hero.
 *
 * Sizing is in `em`, so the mark scales with whatever font-size the caller sets
 * on the host — the nav renders it at 17px, the footer at 15px, and the
 * proportions of the lockup hold at both.
 *
 * The stroke ramp (accent-800 → accent-600 outward-in) is what gives the mark
 * depth without a fill; at favicon sizes those outer strokes drop out entirely.
 * See public/favicon.svg for the reduced small-size cut.
 */
@Component({
  selector: 'app-protofast-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-flex items-center gap-[0.5em]' },
  template: `
    <svg
      class="block h-[1.5em] w-[1.5em] shrink-0"
      viewBox="0 0 32 32"
      fill="none"
      aria-hidden="true"
    >
      <g stroke-width="2.8" stroke-linecap="round">
        <path d="M5 6 H13" stroke="var(--color-accent-800)" />
        <path d="M5 11 H16" stroke="var(--color-accent-700)" />
        <path d="M5 16 H19" stroke="var(--color-accent-600)" />
        <path d="M5 21 H16" stroke="var(--color-accent-700)" />
        <path d="M5 26 H13" stroke="var(--color-accent-800)" />
      </g>
      <circle cx="25" cy="16" r="4" fill="var(--color-accent)" />
    </svg>

    <!-- The wordmark inherits the host's font-size; only the tracking is fixed. -->
    @if (showWordmark()) {
      <span class="font-medium tracking-[-0.02em] whitespace-nowrap">Protofast</span>
    }
  `,
})
export class ProtofastLogo {
  /** Set false for a mark-only lockup (tight headers, app icons). */
  readonly showWordmark = input(true);
}
