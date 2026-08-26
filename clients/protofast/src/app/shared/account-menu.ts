import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/**
 * The signed-in header control — an avatar button that opens a menu holding the two things a
 * signed-in header ever offered: the account page, and the way out.
 *
 * It replaces the "Account" and "Sign out" pair every header used to spell out, so the bar keeps
 * its width for the page's own actions and still fits a 320px phone. Drawn from the Nocturne
 * tokens (see src/styles/nocturne.css), so it sits on the landing page and the account page
 * without a second palette.
 *
 * Closing is handled at the document, not on a backdrop: a full-screen overlay would sit over the
 * sticky header and eat the first click anywhere on the page. `open` starts false, so SSR renders
 * the button alone and nothing flashes on hydration.
 */
@Component({
  selector: 'app-account-menu',
  imports: [RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'relative inline-flex',
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'close(true)',
  },
  template: `
    <button
      #trigger
      type="button"
      class="btn btn-secondary btn-icon rounded-full transition"
      [class]="
        open()
          ? 'border-[var(--color-accent)] text-[var(--color-accent)]'
          : 'text-[var(--color-neutral-300)] hover:text-[var(--color-accent)]'
      "
      aria-haspopup="menu"
      aria-controls="account-menu"
      [attr.aria-expanded]="open()"
      [attr.aria-label]="open() ? 'Close account menu' : 'Account menu'"
      (click)="toggle()"
    >
      <svg
        class="h-[19px] w-[19px]"
        viewBox="0 0 20 20"
        fill="none"
        stroke="currentColor"
        stroke-width="1.5"
        aria-hidden="true"
      >
        <circle cx="10" cy="7.1" r="3.05" />
        <path d="M3.9 16.7a6.35 6.35 0 0 1 12.2 0" stroke-linecap="round" />
      </svg>
    </button>

    <!--
      The panel closes on any click inside it, which covers both rows: the router link leaves this
      component mounted when the header is shared across routes, and the sign-out navigation takes
      a moment the open menu should not sit through.
    -->
    @if (open()) {
      <div
        id="account-menu"
        role="menu"
        class="absolute top-full right-0 z-50 mt-2.5 w-44 rounded-[var(--radius-md)] border border-[var(--color-divider)] bg-[var(--color-surface)] p-1 shadow-[var(--shadow-md)]"
        (click)="close()"
      >
        <!-- The row is a self-link on the account page itself; aria-current says so. -->
        <a
          routerLink="/app/account"
          routerLinkActive
          ariaCurrentWhenActive="page"
          role="menuitem"
          class="account-menu-item"
        >
          Account
        </a>
        <div class="my-1 h-px bg-[var(--color-divider)]" aria-hidden="true"></div>
        <!-- /signout is a BFF endpoint, not an Angular route — full-page navigation. -->
        <a href="/signout" rel="external" role="menuitem" class="account-menu-item">Sign out</a>
      </div>
    }
  `,
})
export class AccountMenu {
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');

  protected readonly open = signal(false);

  protected toggle(): void {
    this.open.update((open) => !open);
  }

  /** `fromKeyboard` returns focus to the trigger, which Escape has to do and a click must not. */
  protected close(fromKeyboard = false): void {
    if (!this.open()) {
      return;
    }
    this.open.set(false);
    if (fromKeyboard) {
      this.trigger().nativeElement.focus();
    }
  }

  protected onDocumentClick(event: MouseEvent): void {
    if (!this.host.nativeElement.contains(event.target as Node)) {
      this.close();
    }
  }
}
