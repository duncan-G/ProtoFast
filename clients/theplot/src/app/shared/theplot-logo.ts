import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The ThePlot lockup — mark plus wordmark.
 *
 * An open book carrying all three things you do here: the left page holds the lines you read and
 * the caret where you write, the right page opens into the triangle you press to watch. The two
 * pages sit a step apart on the accent ramp (600 behind, 500 in front) so the gutter reads without
 * a drawn spine, and the page furniture is accent-200 — the page ink, not another accent.
 *
 * Sizing is in `em`, so the mark scales with whatever font-size the caller sets on the host.
 *
 * Two other cuts of this exist and have to move with it: public/favicon.svg drops the caret (at
 * 16px it reads as a third line rather than a cursor), and the Keycloak login theme redraws the
 * lockup in FreeMarker at infra/keycloak/themes/theplot/login/template.ftl.
 */
@Component({
  selector: 'app-theplot-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-flex items-center gap-[0.5em]' },
  template: `
    <svg class="block h-[1.5em] w-[1.5em] shrink-0" viewBox="0 0 32 32" aria-hidden="true">
      <path
        d="M15 11.2C12.4 9.4 9 8.6 5.4 8.9V22.6C9 22.3 12.4 23.1 15 24.9Z"
        fill="var(--color-accent-600)"
      />
      <path
        d="M17 11.2C19.6 9.4 23 8.6 26.6 8.9V22.6C23 22.3 19.6 23.1 17 24.9Z"
        fill="var(--color-accent-500)"
      />
      <g stroke="var(--color-accent-200)" stroke-width="1.6" stroke-linecap="round">
        <path d="M8.2 13.9 H12.8" />
        <path d="M8.2 17.7 H11" />
      </g>
      <rect x="12.4" y="15.3" width="1.3" height="4.8" rx="0.6" fill="var(--color-accent-100)" />
      <path
        d="M20.6 13.4 24.9 16.6 20.6 19.8Z"
        fill="var(--color-accent-200)"
        stroke="var(--color-accent-200)"
        stroke-width="1.1"
        stroke-linejoin="round"
      />
    </svg>

    <!-- The wordmark inherits the host's font-size; only the tracking is fixed. -->
    @if (showWordmark()) {
      <span class="font-semibold tracking-tight whitespace-nowrap">ThePlot</span>
    }
  `,
})
export class ThePlotLogo {
  /** Set false for a mark-only lockup (tight headers, app icons). */
  readonly showWordmark = input(true);
}
