import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';

/**
 * The operator's avatar menu. Its links leave the console for pages the platform console serves,
 * so they are full-page navigations rather than router links.
 *
 * Closing is handled at the document rather than on a backdrop, which would swallow the first
 * click anywhere on the page.
 */
@Component({
  selector: 'kit-account-menu',
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

    @if (open()) {
      <div
        id="account-menu"
        role="menu"
        class="absolute right-0 top-full z-50 mt-2 w-44 rounded-lg border border-gray-200
               bg-white p-1 shadow-lg"
        (click)="close()"
      >
        <a
          href="/app"
          rel="external"
          role="menuitem"
          class="block rounded-md px-3 py-2 text-sm text-gray-700 hover:bg-gray-50 hover:text-indigo-600"
        >
          Consoles
        </a>
        <a
          href="/app/account"
          rel="external"
          role="menuitem"
          class="block rounded-md px-3 py-2 text-sm text-gray-700 hover:bg-gray-50 hover:text-indigo-600"
        >
          Account
        </a>
        <div class="my-1 h-px bg-gray-200" aria-hidden="true"></div>
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
