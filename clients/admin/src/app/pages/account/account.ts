import {
  ChangeDetectionStrategy,
  Component,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { ConsoleShell } from '../../../admin-kit';
import { ACCOUNT_ROUTE, AccountApi, AccountView } from '../../account/account-api';

/**
 * An operator's own sign-in: email and passkeys. There is no delete — operator accounts are
 * created and revoked with scripts/keycloak-grant-admin.py.
 *
 * Enrolling a passkey is a WebAuthn ceremony on Keycloak's origin, so it is a full-page navigation
 * to a BFF endpoint, never a router link.
 */
@Component({
  selector: 'app-account',
  imports: [ConsoleShell, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <kit-console-shell title="ProtoFast Admin">
      <h1 class="text-2xl font-bold text-gray-900">Account</h1>
      <p class="mt-2 text-gray-600">How you sign in.</p>

      @if (error()) {
        <p class="mt-6 rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">
          {{ error() }}
        </p>
      }

      <!-- ───────────────────────────── email ───────────────────────────── -->
      <section class="mt-8 bg-white rounded-2xl shadow-sm border border-gray-200 p-6 space-y-4">
        <div>
          <h2 class="text-lg font-semibold text-gray-900">Email</h2>
          <p class="mt-1 text-sm text-gray-600">
            The address that reaches your account, and where sign-in codes are sent. Changing it
            takes a code sent to the new address first, so a typo can't lock you out.
          </p>
        </div>

        <p class="text-gray-900 font-medium break-all">
          {{ loading() ? '…' : (view()?.email ?? 'Unknown') }}
        </p>

        @if (emailNotice()) {
          <p class="rounded-lg bg-gray-50 border border-gray-200 px-4 py-3 text-sm text-gray-700">
            {{ emailNotice() }}
          </p>
        }
        @if (emailError()) {
          <p class="rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
            {{ emailError() }}
          </p>
        }

        @switch (emailStep()) {
          @case ('idle') {
            <button
              type="button"
              class="inline-block rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium
                         text-gray-700 hover:bg-gray-50 transition disabled:opacity-50"
              [disabled]="loading()"
              (click)="startEmailChange()"
            >
              Change email
            </button>
          }

          @case ('address') {
            <div class="space-y-1">
              <label for="new-email" class="block text-sm font-medium text-gray-700">
                New email address
              </label>
              <input
                id="new-email"
                class="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm text-gray-900"
                type="email"
                autocomplete="email"
                spellcheck="false"
                [value]="newEmail()"
                (input)="newEmail.set($any($event.target).value)"
                (keyup.enter)="sendEmailCode()"
              />
            </div>

            <div class="flex flex-wrap gap-2">
              <button
                type="button"
                class="rounded-lg bg-gray-900 px-4 py-2 text-sm font-medium text-white
                           hover:bg-gray-800 transition disabled:opacity-50"
                [disabled]="!newEmail().trim() || emailBusy()"
                (click)="sendEmailCode()"
              >
                {{ emailBusy() ? 'Sending…' : 'Send code' }}
              </button>
              <button
                type="button"
                class="rounded-lg px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50 transition"
                (click)="abandonEmailChange()"
              >
                Cancel
              </button>
            </div>
          }

          @case ('code') {
            <p class="text-sm text-gray-600">
              Enter the six-digit code sent to
              <span class="break-all font-medium text-gray-900">{{ pendingEmail() }}</span
              >. It expires in 15 minutes, and nothing changes until you enter it.
            </p>

            <div class="space-y-1">
              <label for="email-code" class="block text-sm font-medium text-gray-700">
                Confirmation code
              </label>
              <input
                id="email-code"
                class="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm text-gray-900"
                type="text"
                inputmode="numeric"
                autocomplete="one-time-code"
                maxlength="6"
                spellcheck="false"
                [value]="emailCode()"
                (input)="emailCode.set($any($event.target).value)"
                (keyup.enter)="confirmEmailChange()"
              />
            </div>

            <div class="flex flex-wrap gap-2">
              <button
                type="button"
                class="rounded-lg bg-gray-900 px-4 py-2 text-sm font-medium text-white
                           hover:bg-gray-800 transition disabled:opacity-50"
                [disabled]="emailCode().trim().length !== 6 || emailBusy()"
                (click)="confirmEmailChange()"
              >
                {{ emailBusy() ? 'Confirming…' : 'Confirm' }}
              </button>
              <button
                type="button"
                class="rounded-lg px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50
                           transition disabled:opacity-50"
                [disabled]="emailBusy()"
                (click)="resendEmailCode()"
              >
                Resend
              </button>
              <button
                type="button"
                class="rounded-lg px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50
                           transition disabled:opacity-50"
                [disabled]="emailBusy()"
                (click)="useDifferentAddress()"
              >
                Use a different address
              </button>
            </div>
          }
        }
      </section>

      <!-- ──────────────────────────── passkeys ─────────────────────────── -->
      <section class="mt-6 bg-white rounded-2xl shadow-sm border border-gray-200 p-6 space-y-4">
        <div>
          <h2 class="text-lg font-semibold text-gray-900">Passkeys</h2>
          <p class="mt-1 text-sm text-gray-600">
            Sign in with your face, fingerprint or device PIN. You can keep several — one per device
            is the usual shape. Removing them all is safe: a mailed code always works.
          </p>
        </div>

        @if (loading()) {
          <p class="text-sm text-gray-500">Loading your passkeys…</p>
        } @else if (view()?.passkeysUnavailable) {
          <p class="text-sm text-gray-600">
            Your passkeys couldn't be loaded just now.
            <button type="button" class="text-indigo-600 hover:underline" (click)="reload()">
              Try again
            </button>
          </p>
        } @else if (passkeys().length === 0) {
          <p class="text-sm text-gray-500">You don't have a passkey yet.</p>
        } @else {
          <ul class="divide-y divide-gray-200 border-y border-gray-200">
            @for (passkey of passkeys(); track passkey.id) {
              <li class="flex items-center gap-4 py-3">
                <div class="min-w-0 flex-1">
                  <p class="truncate font-medium text-gray-900">
                    {{ passkey.label || 'Passkey' }}
                  </p>
                  <p class="text-xs text-gray-500">
                    @if (passkey.createdAt) {
                      Added {{ passkey.createdAt | date: 'mediumDate' }}
                    } @else {
                      Added some time ago
                    }
                    @if (!passkey.passwordless) {
                      · second factor
                    }
                  </p>
                </div>

                @if (confirmingPasskeyId() === passkey.id) {
                  <button
                    type="button"
                    class="shrink-0 rounded-lg bg-red-600 px-3 py-1.5 text-sm font-medium
                               text-white hover:bg-red-700 disabled:opacity-50 transition"
                    [disabled]="busyPasskeyId() === passkey.id"
                    (click)="removePasskey(passkey.id)"
                  >
                    {{ busyPasskeyId() === passkey.id ? 'Removing…' : 'Confirm' }}
                  </button>
                  <button
                    type="button"
                    class="shrink-0 text-sm text-gray-600 hover:underline"
                    (click)="confirmingPasskeyId.set(null)"
                  >
                    Cancel
                  </button>
                } @else {
                  <button
                    type="button"
                    class="shrink-0 rounded-lg border border-gray-300 px-3 py-1.5 text-sm
                               font-medium text-gray-700 hover:bg-gray-50 transition"
                    (click)="confirmingPasskeyId.set(passkey.id)"
                  >
                    Remove
                  </button>
                }
              </li>
            }
          </ul>
        }

        <!-- BFF endpoint, not an Angular route — full-page navigation. -->
        <a
          [href]="addPasskeyUrl"
          rel="external"
          class="inline-block rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white
                     hover:bg-indigo-700 transition"
        >
          Add a passkey
        </a>
      </section>
    </kit-console-shell>
  `,
})
export class Account {
  private readonly api = inject(AccountApi);

  protected readonly view = signal<AccountView | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');

  /**
   * Which step of the email change is on screen. It is not purely local: `reload()` puts the page
   * back on 'code' whenever the server still holds a change waiting for one, so a refresh or a
   * second tab lands where the user left off.
   */
  protected readonly emailStep = signal<'idle' | 'address' | 'code'>('idle');
  protected readonly newEmail = signal('');
  protected readonly emailCode = signal('');
  protected readonly emailBusy = signal(false);
  protected readonly emailError = signal('');
  protected readonly emailNotice = signal('');

  /** The passkey a Remove click has armed, if any — the second tap is the one that acts. */
  protected readonly confirmingPasskeyId = signal<string | null>(null);
  protected readonly busyPasskeyId = signal<string | null>(null);

  protected readonly passkeys = computed(() => this.view()?.passkeys ?? []);

  /**
   * The address a code is waiting on: the server's answer, falling back to what was just typed
   * for the moment between sending and the next `load()`.
   */
  protected readonly pendingEmail = computed(
    () => this.view()?.pendingEmail ?? this.newEmail().trim().toLowerCase(),
  );

  protected readonly addPasskeyUrl = `/add-passkey?returnUrl=${encodeURIComponent(ACCOUNT_ROUTE)}`;

  constructor() {
    // Browser-only: the session cookie never reaches the SSR render, so asking there would
    // paint a signed-out page for a signed-in user and then correct itself on hydration.
    afterNextRender(() => void this.reload());
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const view = await this.api.load();
      this.view.set(view);
      // An email change still waiting on its code — started here before a refresh, or in another
      // tab — puts the page back on the code box rather than offering to start a second one.
      if (view.pendingEmail) {
        this.emailStep.set('code');
      } else if (this.emailStep() === 'code') {
        this.emailStep.set('idle');
      }
    } catch (err) {
      this.error.set(message(err));
    } finally {
      this.loading.set(false);
    }
  }

  protected startEmailChange(): void {
    this.emailCode.set('');
    this.clearEmailMessages();
    if (this.view()?.pendingEmail) {
      this.emailStep.set('code');
      return;
    }
    this.newEmail.set('');
    this.emailStep.set('address');
  }

  /**
   * Asks the BFF to mail a code to the typed address. Nothing about the account changes here —
   * this is the step that proves the mailbox before anything is written to it.
   */
  protected async sendEmailCode(): Promise<void> {
    const target = this.newEmail().trim();
    if (!target || this.emailBusy()) {
      return;
    }

    this.emailBusy.set(true);
    this.clearEmailMessages();
    try {
      const pending = await this.api.requestEmailChange(target);
      this.emailCode.set('');
      this.emailStep.set('code');
      this.emailNotice.set(
        pending.sent === false
          ? `Enter the code already sent to ${pending.email}.`
          : `Code sent to ${pending.email}.`,
      );
      // Re-read so the pending address on the view is the server's, not this page's guess.
      this.view.set(await this.api.load());
    } catch (err) {
      this.emailError.set(message(err));
    } finally {
      this.emailBusy.set(false);
    }
  }

  /** Sends another code to the same address — the server's, so a resend cannot retarget it. */
  protected async resendEmailCode(): Promise<void> {
    this.newEmail.set(this.pendingEmail());
    await this.sendEmailCode();
  }

  protected async confirmEmailChange(): Promise<void> {
    const code = this.emailCode().trim();
    if (code.length !== 6 || this.emailBusy()) {
      return;
    }

    this.emailBusy.set(true);
    this.clearEmailMessages();
    try {
      await this.api.confirmEmailChange(code);
      // The address the BFF wrote is the one it mailed, which this page can only learn by asking.
      const view = await this.api.load();
      this.view.set(view);
      this.emailStep.set('idle');
      this.emailCode.set('');
      this.newEmail.set('');
      this.emailNotice.set(`Your email address is now ${view.email}.`);
    } catch (err) {
      this.emailError.set(message(err));
    } finally {
      this.emailBusy.set(false);
    }
  }

  /** Closes the form. A mailed code stays parked so it can still be entered. */
  protected abandonEmailChange(): void {
    this.clearEmailMessages();
    this.emailStep.set('idle');
    this.newEmail.set('');
    this.emailCode.set('');
  }

  /** Keeps the mailed code; sending a different address is what replaces it. */
  protected useDifferentAddress(): void {
    this.clearEmailMessages();
    this.emailCode.set('');
    this.newEmail.set('');
    this.emailStep.set('address');
  }

  private clearEmailMessages(): void {
    this.emailError.set('');
    this.emailNotice.set('');
  }

  protected async removePasskey(credentialId: string): Promise<void> {
    this.busyPasskeyId.set(credentialId);
    this.error.set('');
    try {
      await this.api.removePasskey(credentialId);
      // Re-read rather than splicing the row out: the list is Keycloak's, and this is the one
      // moment we know it just changed.
      this.view.set(await this.api.load());
    } catch (err) {
      this.error.set(message(err));
    } finally {
      this.busyPasskeyId.set(null);
      this.confirmingPasskeyId.set(null);
    }
  }
}

function message(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
