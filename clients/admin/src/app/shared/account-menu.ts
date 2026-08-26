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
 * It replaces the "Account" and "Sign out" pair every header used to spell out. Same behaviour as
 * the product site's menu (clients/protofast/src/app/shared/account-menu.ts), drawn instead in the
 * admin console's light palette — the two clients share no code, only the pattern.
 *
 * Closing is handled at the document rather than on a backdrop, which would swallow the first
 * click anywhere on the page. `open` starts false, so SSR renders the button alone and nothing
 * flashes on hydration.
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
      class="flex h-9 w-9 items-center justify-center rounded-full border bg-white transition
             focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-200"
      [class]="
        open()
          ? 'border-indigo-500 text-indigo-600'
          : 'border-gray-300 text-gray-500 hover:border-indigo-400 hover:text-indigo-600'
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
        class="absolute right-0 top-full z-50 mt-2 w-44 rounded-lg border border-gray-200
               bg-white p-1 shadow-lg"
        (click)="close()"
      >
        <!-- The row is a self-link on the account page itself; aria-current says so. -->
        <a
          routerLink="/app/account"
          routerLinkActive
          ariaCurrentWhenActive="page"
          role="menuitem"
          class="block rounded-md px-3 py-2 text-sm text-gray-700 hover:bg-gray-50 hover:text-indigo-600"
        >
          Account
        </a>
        <div class="my-1 h-px bg-gray-200" aria-hidden="true"></div>
        <!-- /signout is a BFF endpoint, not an Angular route — full-page navigation. -->
        <a
          href="/signout"
          rel="external"
          role="menuitem"
          class="block rounded-md px-3 py-2 text-sm text-gray-700 hover:bg-gray-50 hover:text-indigo-600"
        >
          Sign out
        </a>
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
