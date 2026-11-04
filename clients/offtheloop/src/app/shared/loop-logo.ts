import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** The loop that breaks into an arrow, and the mono wordmark. The Keycloak theme draws the same. */
@Component({
  selector: 'app-loop-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-flex items-center gap-2.5' },
  template: `
    <svg width="26" height="18" viewBox="0 0 28 20" aria-hidden="true">
      <path
        d="M13.2 13A6 6 0 1 1 12.8 6.4"
        fill="none"
        stroke="var(--color-text)"
        stroke-width="2.2"
        stroke-linecap="round"
      />
      <path
        d="M12.8 6.4A1.5 1.5 0 0 0 14 7H26M22.5 3.5 26 7l-3.5 3.5"
        fill="none"
        stroke="var(--color-accent)"
        stroke-width="2.2"
        stroke-linecap="round"
        stroke-linejoin="round"
      />
    </svg>
    @if (wordmark()) {
      <span class="mono text-[16px] font-semibold tracking-[-0.02em] text-[var(--color-text)]"
        >offtheloop</span
      >
    }
  `,
})
export class LoopLogo {
  readonly wordmark = input(false);
}
