import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { ACCOUNT_ROUTE, AccountApi, AccountView } from '../../account/account-api';
import { Icon } from '../../shared/icon';

const RESEND_COOLDOWN_SECONDS = 60;

/** Email, passkeys and leaving, all against auth's /account endpoints on this origin. */
@Component({
  selector: 'app-account',
  imports: [DatePipe, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto flex max-w-[640px] flex-col gap-10">
      @if (deleted()) {
        <section class="flex flex-col items-start gap-3">
          <h1 class="display">Account deleted</h1>
          <p class="m-0 text-[var(--color-muted)]">
            {{ view()?.email ?? 'Your account' }} is gone, and you’re signed out everywhere.
          </p>
          <a href="/" rel="external" class="btn">Back to offtheloop</a>
        </section>
      } @else {
        <header class="flex flex-wrap items-end justify-between gap-4">
          <div>
            <p class="label m-0">account</p>
            <h1 class="display mt-1.5">{{ loading() ? '…' : (view()?.email ?? 'Account') }}</h1>
          </div>
          <a href="/signout" rel="external" class="btn">Sign out</a>
        </header>

        @if (error()) {
          <p class="alert" role="alert">{{ error() }}</p>
        }

        <section aria-labelledby="email-h" class="section-rule flex flex-col gap-4">
          <h2 id="email-h" class="label m-0">email</h2>
          @switch (emailStep()) {
            @case ('idle') {
              <div class="flex flex-wrap items-center justify-between gap-3">
                <span class="text-[16px] [overflow-wrap:anywhere]">{{ view()?.email ?? '…' }}</span>
                <button
                  type="button"
                  class="btn"
                  [disabled]="loading()"
                  (click)="startEmailChange()"
                >
                  Change
                </button>
              </div>
            }
            @case ('address') {
              <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); sendEmailCode()">
                <label class="field max-w-[380px]">
                  <span>new email</span>
                  <input
                    class="input"
                    type="email"
                    autocomplete="email"
                    spellcheck="false"
                    placeholder="you@example.com"
                    [value]="newEmail()"
                    [disabled]="emailBusy()"
                    (input)="newEmail.set($any($event.target).value)"
                  />
                </label>
                <div class="flex flex-wrap gap-2.5">
                  <button
                    type="submit"
                    class="btn btn-primary"
                    [disabled]="!newEmailValid() || emailBusy()"
                  >
                    Send code
                  </button>
                  <button
                    type="button"
                    class="btn"
                    [disabled]="emailBusy()"
                    (click)="resetEmailChange()"
                  >
                    Cancel
                  </button>
                </div>
              </form>
            }
            @case ('code') {
              <form
                class="flex flex-col gap-4"
                (submit)="$event.preventDefault(); confirmEmailChange()"
              >
                <p class="m-0 text-[var(--color-muted)]">
                  Enter the code we sent to
                  <span class="text-[var(--color-text)] [overflow-wrap:anywhere]">{{
                    pendingEmail()
                  }}</span
                  >.
                </p>
                <label class="field max-w-[240px]">
                  <span>code</span>
                  <input
                    class="input mono h-14 text-[28px] tracking-[0.4em]"
                    type="text"
                    inputmode="numeric"
                    autocomplete="one-time-code"
                    maxlength="6"
                    placeholder="000000"
                    [value]="emailCode()"
                    [disabled]="emailBusy()"
                    (input)="setEmailCode($any($event.target))"
                  />
                </label>
                <div class="flex flex-wrap items-center gap-2.5">
                  <button
                    type="submit"
                    class="btn btn-primary"
                    [disabled]="emailCode().length !== 6 || emailBusy()"
                  >
                    Confirm change
                  </button>
                  <button
                    type="button"
                    class="btn"
                    [disabled]="emailBusy()"
                    (click)="resetEmailChange()"
                  >
                    Cancel
                  </button>
                  <span class="flex-1"></span>
                  @if (resendIn() > 0) {
                    <span class="mono text-[12px] text-[var(--color-dim)]"
                      >resend in {{ resendIn() }}s</span
                    >
                  } @else {
                    <button
                      type="button"
                      class="btn-link"
                      [disabled]="emailBusy()"
                      (click)="resendEmailCode()"
                    >
                      resend code
                    </button>
                  }
                </div>
              </form>
            }
          }
          @if (emailError()) {
            <p class="alert" role="alert">{{ emailError() }}</p>
          }
          <p class="m-0 text-[13.5px] text-[var(--color-dim)]">
            Sign-in codes go here. A new address is confirmed before it replaces this one.
          </p>
        </section>

        <section aria-labelledby="passkeys-h" class="section-rule flex flex-col gap-2">
          <h2 id="passkeys-h" class="label m-0 mb-2">passkeys</h2>
          @if (loading()) {
            <p class="m-0 text-[var(--color-dim)]">Loading…</p>
          } @else if (!view()) {
            <p class="m-0 text-[var(--color-dim)]">—</p>
          } @else if (view()?.passkeysUnavailable) {
            <p class="m-0 flex flex-wrap items-center gap-3 text-[var(--color-muted)]">
              Your passkeys couldn’t be loaded.
              <button type="button" class="btn-link" (click)="reload()">try again</button>
            </p>
          } @else {
            @for (passkey of view()?.passkeys ?? []; track passkey.id) {
              <div class="row min-h-[56px] flex-wrap gap-3.5 py-2">
                <span
                  class="flex h-[34px] w-[34px] flex-none items-center justify-center rounded-full bg-[var(--color-line)] text-[var(--color-accent)]"
                  ><app-icon name="key" [size]="16"
                /></span>
                <span class="flex min-w-0 flex-1 flex-col">
                  <span class="truncate font-medium">{{ passkey.label || 'Passkey' }}</span>
                  <span class="mono text-[11.5px] text-[var(--color-dim)]">
                    @if (passkey.createdAt) {
                      added {{ passkey.createdAt | date: 'MMM d, y' }}
                    } @else {
                      added a while ago
                    }
                  </span>
                </span>
                @if (confirmingPasskeyId() === passkey.id) {
                  <span class="flex items-center gap-2.5">
                    <button
                      type="button"
                      class="btn btn-danger"
                      [disabled]="busyPasskeyId() === passkey.id"
                      (click)="removePasskey(passkey.id)"
                    >
                      Remove
                    </button>
                    <button type="button" class="btn-link" (click)="confirmingPasskeyId.set(null)">
                      keep
                    </button>
                  </span>
                } @else {
                  <button
                    type="button"
                    class="btn-link"
                    (click)="confirmingPasskeyId.set(passkey.id)"
                  >
                    remove
                  </button>
                }
              </div>
            } @empty {
              <p class="m-0 text-[var(--color-dim)]">
                No passkeys yet. You’ll sign in with a mailed code.
              </p>
            }
            <a [href]="addPasskeyUrl" rel="external" class="btn mt-3 self-start pl-3.5">
              <app-icon name="plus" [size]="14" [weight]="2" />Add a passkey
            </a>
          }
          <p class="m-0 mt-2 text-[13.5px] text-[var(--color-dim)]">
            Face, fingerprint or device PIN, one per device. It stays on the device; we never see
            it.
          </p>
        </section>

        <section aria-labelledby="leave-h" class="section-rule flex flex-col gap-4">
          <h2 id="leave-h" class="label m-0">leaving</h2>
          @if (!deleteOpen()) {
            <button type="button" class="btn btn-danger self-start" (click)="deleteOpen.set(true)">
              Delete my account…
            </button>
          } @else {
            <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); deleteAccount()">
              <p class="m-0 text-[var(--color-muted)]">
                Your sign-in and passkeys go at once and for good. Type your email to confirm.
              </p>
              <label class="field max-w-[380px]">
                <span>email</span>
                <input
                  class="input"
                  type="email"
                  autocomplete="off"
                  spellcheck="false"
                  [placeholder]="view()?.email ?? ''"
                  [value]="deleteConfirmation()"
                  [disabled]="deleting()"
                  (input)="deleteConfirmation.set($any($event.target).value)"
                />
              </label>
              <div class="flex flex-wrap gap-2.5">
                <button
                  type="submit"
                  class="btn btn-danger"
                  [disabled]="!confirmationMatches() || deleting()"
                >
                  Delete forever
                </button>
                <button
                  type="button"
                  class="btn"
                  [disabled]="deleting()"
                  (click)="deleteOpen.set(false)"
                >
                  Keep my account
                </button>
              </div>
            </form>
          }
        </section>
      }
    </div>
  `,
})
export class Account {
  private readonly api = inject(AccountApi);
  private resendTimer: ReturnType<typeof setInterval> | null = null;

  protected readonly view = signal<AccountView | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');

  protected readonly emailStep = signal<'idle' | 'address' | 'code'>('idle');
  protected readonly newEmail = signal('');
  protected readonly emailCode = signal('');
  protected readonly emailBusy = signal(false);
  protected readonly emailError = signal('');
  protected readonly resendIn = signal(0);

  protected readonly confirmingPasskeyId = signal<string | null>(null);
  protected readonly busyPasskeyId = signal<string | null>(null);

  protected readonly deleteOpen = signal(false);
  protected readonly deleteConfirmation = signal('');
  protected readonly deleting = signal(false);
  protected readonly deleted = signal(false);

  protected readonly addPasskeyUrl = `/add-passkey?returnUrl=${encodeURIComponent(ACCOUNT_ROUTE)}`;

  protected readonly newEmailValid = computed(() =>
    /^[^@\s]+@[^@\s]+\.[^@\s]{2,}$/.test(this.newEmail().trim()),
  );
  protected readonly pendingEmail = computed(
    () => this.view()?.pendingEmail ?? this.newEmail().trim().toLowerCase(),
  );
  protected readonly confirmationMatches = computed(() => {
    const email = this.view()?.email?.trim().toLowerCase();
    return !!email && this.deleteConfirmation().trim().toLowerCase() === email;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopResendCooldown());
    // The session cookie never reaches the render, so this asks from the browser.
    afterNextRender(() => void this.reload());
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const view = await this.api.load();
      this.view.set(view);
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
    this.emailError.set('');
    if (this.view()?.pendingEmail) {
      this.emailStep.set('code');
      return;
    }
    this.newEmail.set('');
    this.emailStep.set('address');
  }

  protected async sendEmailCode(): Promise<void> {
    if (!this.newEmailValid() || this.emailBusy()) {
      return;
    }
    this.emailBusy.set(true);
    this.emailError.set('');
    try {
      const pending = await this.api.requestEmailChange(this.newEmail().trim());
      this.emailCode.set('');
      this.emailStep.set('code');
      if (pending.sent !== false) {
        this.startResendCooldown();
      }
      this.view.set(await this.api.load());
    } catch (err) {
      this.emailError.set(message(err));
    } finally {
      this.emailBusy.set(false);
    }
  }

  protected setEmailCode(field: HTMLInputElement): void {
    const digits = field.value.replace(/\D/g, '').slice(0, 6);
    field.value = digits;
    this.emailCode.set(digits);
  }

  protected async resendEmailCode(): Promise<void> {
    this.newEmail.set(this.pendingEmail());
    await this.sendEmailCode();
  }

  protected async confirmEmailChange(): Promise<void> {
    if (this.emailCode().length !== 6 || this.emailBusy()) {
      return;
    }
    this.emailBusy.set(true);
    this.emailError.set('');
    try {
      await this.api.confirmEmailChange(this.emailCode());
      this.view.set(await this.api.load());
      this.resetEmailChange();
    } catch (err) {
      this.emailError.set(message(err));
    } finally {
      this.emailBusy.set(false);
    }
  }

  /** A code already mailed stays valid; Change goes back to it. */
  protected resetEmailChange(): void {
    this.stopResendCooldown();
    this.emailStep.set('idle');
    this.newEmail.set('');
    this.emailCode.set('');
  }

  protected async removePasskey(credentialId: string): Promise<void> {
    this.busyPasskeyId.set(credentialId);
    this.error.set('');
    try {
      await this.api.removePasskey(credentialId);
      this.view.set(await this.api.load());
    } catch (err) {
      this.error.set(message(err));
    } finally {
      this.confirmingPasskeyId.set(null);
      this.busyPasskeyId.set(null);
    }
  }

  protected async deleteAccount(): Promise<void> {
    if (!this.confirmationMatches() || this.deleting()) {
      return;
    }
    this.deleting.set(true);
    this.error.set('');
    try {
      await this.api.deleteAccount();
      this.deleted.set(true);
      window.location.replace('/');
    } catch (err) {
      this.error.set(message(err));
      this.deleting.set(false);
    }
  }

  private startResendCooldown(): void {
    this.stopResendCooldown();
    this.resendIn.set(RESEND_COOLDOWN_SECONDS);
    this.resendTimer = setInterval(() => {
      this.resendIn.update((secs) => secs - 1);
      if (this.resendIn() <= 0) {
        this.stopResendCooldown();
      }
    }, 1000);
  }

  private stopResendCooldown(): void {
    if (this.resendTimer !== null) {
      clearInterval(this.resendTimer);
      this.resendTimer = null;
    }
    this.resendIn.set(0);
  }
}

function message(err: unknown): string {
  return err instanceof Error ? err.message : 'Something went wrong.';
}
