import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ProtofastLogo } from '../../shared/protofast-logo';
import { AccountMenu } from '../../shared/account-menu';
import { ACCOUNT_ROUTE, AccountApi, AccountView } from '../../account/account-api';

/**
 * Account management — the three things a user can do to their own account without asking anyone.
 *
 * All of it happens here, on our own origin. Changing the email address is a two-step form
 * against the BFF (ask for a code, send it back); removing a passkey and deleting the account are
 * single calls. Keycloak's account console is never linked to and is not expected to be reachable.
 *
 * The one exception is enrolling a passkey, which is a WebAuthn ceremony that needs Keycloak's own
 * origin — a BFF endpoint reached by full-page navigation, never a router link.
 *
 * Every section is the same shape (`.split` in src/styles/nocturne.css): the explanation on the
 * left, the controls on the right. Each feature expands where it stands — no modals, and the only
 * route change in the page is the passkey hand-off, which has to leave. Because a section's
 * heading sits in its own column, a form opening below it moves nothing but itself.
 */
@Component({
  selector: 'app-account',
  imports: [AccountMenu, DatePipe, RouterLink, ProtofastLogo],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="nocturne flex min-h-screen flex-col antialiased">
      <header class="border-b border-[var(--color-divider)]">
        <nav class="mx-auto flex w-full max-w-[880px] items-center px-6 py-4 sm:px-10">
          <a routerLink="/app" class="mr-auto no-underline"><app-protofast-logo /></a>
          <!-- Account and sign-out live behind the avatar; see shared/account-menu.ts. -->
          <app-account-menu />
        </nav>
      </header>

      <main class="mx-auto w-full max-w-[880px] flex-1 px-6 pt-[38px] pb-[56px] sm:px-10">
        @if (deleted()) {
          <div class="card elev-sm items-start gap-2.5 px-5 py-[26px]">
            <h1 class="text-[19px]">Account deleted</h1>
            <p class="m-0 max-w-[380px] text-muted text-[13.5px] leading-[1.55]">
              {{ view()?.email ?? 'Your account' }} is gone, along with everything stored under it.
              You've been signed out on every device.
            </p>
            <!-- The redirect leaves this page at once; the link is the way out if it cannot. -->
            <a href="/" rel="external" class="btn btn-secondary mt-1">Back to protofast.dev</a>
          </div>
        } @else {
          <h1 class="mb-1.5 text-[38px]">Account</h1>
          <p class="mb-[30px] text-muted text-[14.5px]">How you sign in, and how to leave.</p>

          @if (error()) {
            <p class="panel-danger mb-[26px] text-[13px]" role="alert">{{ error() }}</p>
          }

          <!-- ───────────────────────────── email ───────────────────────────── -->
          <section class="split">
            <div class="split-aside">
              <h2 class="text-[17px]">Email</h2>
              <p class="m-0 text-muted text-[12.5px] leading-[1.5]">
                The address that reaches your account and receives sign-in codes. A change is
                confirmed at the new address first, so a typo can't lock you out.
              </p>
            </div>

            <div class="split-main flex flex-col gap-[14px]">
              <div
                class="flex flex-wrap items-center gap-3 rounded-[var(--radius-md)] bg-[var(--color-surface)] px-[15px] py-[13px] shadow-[var(--shadow-sm)]"
              >
                <div class="min-w-0 flex-[1_1_200px]">
                  <p class="card-kicker">Signed in as</p>
                  <p class="mt-[3px] mb-0 text-[15.5px] [overflow-wrap:anywhere]">
                    {{ loading() ? '…' : (view()?.email ?? 'Unknown') }}
                  </p>
                </div>
                @if (emailStep() === 'idle') {
                  <button
                    type="button"
                    class="btn btn-secondary"
                    [disabled]="loading()"
                    (click)="startEmailChange()"
                  >
                    Change email
                  </button>
                }
              </div>

              @if (emailError()) {
                <p class="panel-danger text-[13px]" role="alert">{{ emailError() }}</p>
              }

              @if (emailStep() === 'address') {
                <div class="panel rise flex flex-col gap-3">
                  <div class="field">
                    <label for="new-email">New email address</label>
                    <input
                      id="new-email"
                      class="input max-w-[340px]"
                      type="email"
                      placeholder="you@example.com"
                      autocomplete="email"
                      spellcheck="false"
                      [value]="newEmail()"
                      [disabled]="emailBusy()"
                      (input)="newEmail.set($any($event.target).value)"
                      (keyup.enter)="sendEmailCode()"
                    />
                  </div>
                  <p class="m-0 text-muted text-[12.5px] leading-[1.5]">
                    We'll send a six-digit code there. Your current address keeps working until the
                    new one is confirmed.
                  </p>
                  <div class="flex flex-wrap items-center gap-2">
                    <button
                      type="button"
                      class="btn btn-primary"
                      [disabled]="!newEmailValid() || emailBusy()"
                      (click)="sendEmailCode()"
                    >
                      @if (emailBusy()) {
                        <span class="spinner" aria-hidden="true"></span>
                      }
                      {{ emailBusy() ? 'Sending code' : 'Send code' }}
                    </button>
                    <button
                      type="button"
                      class="btn btn-ghost"
                      [disabled]="emailBusy()"
                      (click)="abandonEmailChange()"
                    >
                      Cancel
                    </button>
                  </div>
                </div>
              }

              @if (emailStep() === 'code') {
                <div class="panel rise flex flex-col gap-[13px]">
                  <p class="m-0 text-[13.5px] leading-[1.5]">
                    Enter the code we sent to
                    <span class="text-[var(--color-accent-300)] [overflow-wrap:anywhere]">{{
                      pendingEmail()
                    }}</span>
                  </p>

                  <!--
                    The six boxes are presentation; the field is the one input lying across them.
                    Keeping it a single field is what lets paste, autofill and the one-time-code
                    keyboard work at all — six inputs would break every one of them.
                  -->
                  <div class="relative w-max max-w-full">
                    <div class="code-cells" aria-hidden="true">
                      @for (cell of codeCells(); track $index) {
                        <div class="code-cell">{{ cell }}</div>
                      }
                    </div>
                    <input
                      #codeField
                      class="code-input"
                      type="text"
                      inputmode="numeric"
                      autocomplete="one-time-code"
                      maxlength="6"
                      spellcheck="false"
                      aria-label="Confirmation code"
                      [value]="emailCode()"
                      [disabled]="emailBusy()"
                      (input)="setEmailCode(codeField)"
                      (keyup.enter)="confirmEmailChange()"
                    />
                  </div>

                  <div class="flex flex-wrap items-center gap-2">
                    <button
                      type="button"
                      class="btn btn-primary"
                      [disabled]="emailCode().length !== 6 || emailBusy()"
                      (click)="confirmEmailChange()"
                    >
                      @if (emailBusy()) {
                        <span class="spinner" aria-hidden="true"></span>
                      }
                      {{ emailBusy() ? 'Confirming' : 'Confirm change' }}
                    </button>
                    <button
                      type="button"
                      class="btn btn-ghost"
                      [disabled]="emailBusy()"
                      (click)="abandonEmailChange()"
                    >
                      Cancel
                    </button>
                    <span class="flex-1"></span>
                    @if (resendIn() > 0) {
                      <span class="text-muted text-[12.5px]">{{ resendLabel() }}</span>
                    } @else {
                      <button
                        type="button"
                        class="btn btn-ghost text-[12.5px]"
                        [disabled]="emailBusy()"
                        (click)="resendEmailCode()"
                      >
                        Resend code
                      </button>
                    }
                  </div>
                </div>
              }
            </div>
          </section>

          <hr class="hr my-[26px]" />

          <!-- ──────────────────────────── passkeys ─────────────────────────── -->
          <section class="split">
            <div class="split-aside">
              <h2 class="text-[17px]">Passkeys</h2>
              <p class="m-0 text-muted text-[12.5px] leading-[1.5]">
                Sign in with your face, fingerprint or device PIN. Keep one per device. Removing
                them all is safe — a mailed code always works.
              </p>
            </div>

            <div class="split-main flex flex-col gap-[14px]">
              @if (loading()) {
                <p class="m-0 text-muted text-[13px]">Loading your passkeys…</p>
              } @else if (view()?.passkeysUnavailable) {
                <div class="empty-slot flex flex-wrap items-center gap-[14px]">
                  <div class="min-w-0 flex-[1_1_190px]">
                    <p class="m-0 text-[14.5px]">Your passkeys couldn't be loaded</p>
                    <p class="m-0 text-muted text-[12.5px]">
                      Everything else on this page is still current.
                    </p>
                  </div>
                  <button type="button" class="btn btn-secondary" (click)="reload()">
                    Try again
                  </button>
                </div>
              } @else if (passkeys().length === 0) {
                <div class="empty-slot flex flex-wrap items-center gap-[14px]">
                  <svg
                    class="h-[26px] w-[26px] shrink-0"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="var(--color-accent)"
                    stroke-width="1.5"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    aria-hidden="true"
                  >
                    <circle cx="8" cy="12" r="3.4" />
                    <path d="M11.4 12H21" />
                    <path d="M17.5 12v3.2" />
                    <path d="M20.2 12v2.2" />
                  </svg>
                  <div class="min-w-0 flex-[1_1_190px]">
                    <p class="m-0 text-[14.5px]">No passkeys yet</p>
                    <p class="m-0 text-muted text-[12.5px]">
                      Add one and this device signs you in without a code.
                    </p>
                  </div>
                  <!-- /add-passkey is a BFF endpoint, not an Angular route — full-page navigation. -->
                  <a
                    [href]="addPasskeyUrl"
                    rel="external"
                    class="btn btn-primary"
                    (click)="rememberPasskeysBeforeEnrolment()"
                  >
                    Add a passkey
                  </a>
                </div>
              } @else {
                <ul
                  class="m-0 flex list-none flex-col overflow-hidden rounded-[var(--radius-md)] bg-[var(--color-surface)] p-0 shadow-[var(--shadow-sm)]"
                >
                  @for (passkey of passkeys(); track passkey.id) {
                    <li
                      class="flex flex-col border-t border-[color-mix(in_srgb,var(--color-text)_7%,transparent)]"
                    >
                      <div class="flex flex-wrap items-center gap-3 px-[15px] py-3">
                        <svg
                          class="h-[19px] w-[19px] shrink-0 opacity-65"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="1.5"
                          stroke-linecap="round"
                          stroke-linejoin="round"
                          aria-hidden="true"
                        >
                          <circle cx="8" cy="12" r="3.4" />
                          <path d="M11.4 12H21" />
                          <path d="M17.5 12v3.2" />
                          <path d="M20.2 12v2.2" />
                        </svg>
                        <div class="min-w-0 flex-[1_1_170px]">
                          <p class="m-0 flex flex-wrap items-center gap-2 text-[14.5px]">
                            <span class="truncate">{{ passkey.label || 'Passkey' }}</span>
                            @if (passkey.isNew) {
                              <span class="tag tag-accent">New</span>
                            }
                          </p>
                          <p class="card-meta m-0 text-[12px]">
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

                        @if (busyPasskeyId() === passkey.id) {
                          <span class="inline-flex items-center gap-[7px] text-muted text-[12.5px]">
                            <span class="spinner" aria-hidden="true"></span>Removing
                          </span>
                        } @else if (confirmingPasskeyId() !== passkey.id) {
                          <button
                            type="button"
                            class="btn btn-ghost btn-danger text-[13px]"
                            (click)="confirmingPasskeyId.set(passkey.id)"
                          >
                            Delete
                          </button>
                        }
                      </div>

                      <!-- The confirmation opens under the row it is asking about, not over it. -->
                      @if (confirmingPasskeyId() === passkey.id) {
                        <div
                          class="strip-danger rise flex flex-wrap items-center gap-3 px-[15px] py-3"
                        >
                          <p class="m-0 min-w-0 flex-[1_1_210px] text-[13px] leading-[1.5]">
                            Delete this passkey? Signing in from
                            {{ passkey.label || 'that device' }} will need a mailed code again.
                          </p>
                          <div class="flex gap-2">
                            <button
                              type="button"
                              class="btn btn-danger"
                              (click)="removePasskey(passkey.id)"
                            >
                              Delete passkey
                            </button>
                            <button
                              type="button"
                              class="btn btn-ghost text-[var(--color-text)]"
                              (click)="confirmingPasskeyId.set(null)"
                            >
                              Keep
                            </button>
                          </div>
                        </div>
                      }
                    </li>
                  }
                </ul>
                <div>
                  <a
                    [href]="addPasskeyUrl"
                    rel="external"
                    class="btn btn-primary"
                    (click)="rememberPasskeysBeforeEnrolment()"
                  >
                    Add another passkey
                  </a>
                </div>
              }
            </div>
          </section>

          <hr class="hr my-[26px]" />

          <!-- ────────────────────────── delete account ─────────────────────── -->
          <section class="split">
            <div class="split-aside">
              <h2 class="text-[17px]">Delete account</h2>
              <p class="m-0 text-muted text-[12.5px] leading-[1.5]">
                Removes your sign-in and everything we hold for you, immediately and for good. No
                undo, no grace period.
              </p>
            </div>

            <div class="split-main">
              @if (deleteStep() === 'form') {
                <div class="panel-danger flex flex-col gap-[13px]">
                  <div class="field">
                    <label for="delete-confirmation">
                      Type
                      <span class="text-[var(--color-text)] [overflow-wrap:anywhere]">{{
                        view()?.email ?? 'your email address'
                      }}</span>
                      to confirm
                    </label>
                    <input
                      id="delete-confirmation"
                      class="input max-w-[340px]"
                      type="email"
                      placeholder="your email address"
                      autocomplete="off"
                      spellcheck="false"
                      [value]="deleteConfirmation()"
                      (input)="deleteConfirmation.set($any($event.target).value)"
                    />
                  </div>
                  <div>
                    <button
                      type="button"
                      class="btn btn-danger"
                      [disabled]="!confirmationMatches()"
                      (click)="deleteStep.set('confirm')"
                    >
                      Delete my account
                    </button>
                  </div>
                </div>
              } @else {
                <div class="panel-danger panel-danger-armed rise flex flex-col gap-[13px]">
                  <div class="flex items-start gap-2.5">
                    <svg
                      class="mt-0.5 h-[18px] w-[18px] shrink-0 text-[var(--color-danger-300)]"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      stroke-width="1.7"
                      stroke-linecap="round"
                      stroke-linejoin="round"
                      aria-hidden="true"
                    >
                      <path d="M12 4.5 21 19.5H3z" />
                      <path d="M12 10v4" />
                      <path d="M12 17h.01" />
                    </svg>
                    <div class="min-w-0">
                      <p class="m-0 text-[15px] text-[var(--color-danger-200)]">
                        Delete {{ view()?.email }}?
                      </p>
                      <p class="mt-[3px] mb-0 text-muted text-[13px] leading-[1.5]">
                        Your projects, exports and passkeys go with it. We can't bring any of it
                        back.
                      </p>
                    </div>
                  </div>
                  <div class="flex flex-wrap items-center gap-2">
                    <button
                      type="button"
                      class="btn btn-danger"
                      [disabled]="deleting()"
                      (click)="deleteAccount()"
                    >
                      @if (deleting()) {
                        <span class="spinner" aria-hidden="true"></span>
                      }
                      {{ deleting() ? 'Deleting' : 'Yes, delete everything' }}
                    </button>
                    <button
                      type="button"
                      class="btn btn-ghost text-[var(--color-text)]"
                      [disabled]="deleting()"
                      (click)="cancelDelete()"
                    >
                      Keep my account
                    </button>
                  </div>
                </div>
              }
            </div>
          </section>
        }
      </main>
    </div>
  `,
})
export class Account {
  private readonly api = inject(AccountApi);

  protected readonly view = signal<AccountView | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly deleted = signal(false);
  protected readonly deleting = signal(false);
  protected readonly deleteConfirmation = signal('');

  /**
   * Deleting takes two deliberate acts, not one: typing the address arms the button, and the
   * button swaps the form for a confirmation that spells out what goes. The first step proves the
   * user knows which account this is; the second proves they meant it.
   */
  protected readonly deleteStep = signal<'form' | 'confirm'>('form');

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

  /** Seconds left before another code may be asked for; 0 means the resend is offered. */
  protected readonly resendIn = signal(0);
  private resendTimer: ReturnType<typeof setInterval> | null = null;

  /** The passkey a Delete click has armed, if any — the second tap is the one that acts. */
  protected readonly confirmingPasskeyId = signal<string | null>(null);
  protected readonly busyPasskeyId = signal<string | null>(null);

  /**
   * The credentials this browser had when it last left for Keycloak's enrolment ceremony, or null
   * if it has not been. Anything on the account that is not in here came back with the user, which
   * is the only way this page can tell — the return from `/add-passkey` is a plain redirect and
   * says nothing about what, if anything, was enrolled.
   */
  private readonly knownBeforeEnrolment = signal<ReadonlySet<string> | null>(null);

  protected readonly passkeys = computed(() => {
    const known = this.knownBeforeEnrolment();
    return (this.view()?.passkeys ?? []).map((passkey) => ({
      ...passkey,
      isNew: !!known && !known.has(passkey.id),
    }));
  });

  /** The six boxes, padded out so the empty ones still draw. */
  protected readonly codeCells = computed(() => {
    const code = this.emailCode();
    return Array.from({ length: 6 }, (_, i) => code[i] ?? '');
  });

  /**
   * Enough of an address to be worth mailing. The real check is the mailbox answering, so this
   * only has to stop the obvious slip before a request goes out.
   */
  protected readonly newEmailValid = computed(() =>
    /^[^@\s]+@[^@\s]+\.[^@\s]{2,}$/.test(this.newEmail().trim()),
  );

  protected readonly resendLabel = computed(() => {
    const secs = this.resendIn();
    return `Resend in ${Math.floor(secs / 60)}:${String(secs % 60).padStart(2, '0')}`;
  });

  /**
   * The address a code is waiting on: the server's answer, falling back to what was just typed
   * for the moment between sending and the next `load()`.
   */
  protected readonly pendingEmail = computed(
    () => this.view()?.pendingEmail ?? this.newEmail().trim().toLowerCase(),
  );

  /**
   * Deleting is irreversible and the button sits under a heading that says so, so it stays inert
   * until the user has typed the address the account is reached at. Case and stray spaces are not
   * the point of the exercise.
   */
  protected readonly confirmationMatches = computed(() => {
    const email = this.view()?.email?.trim().toLowerCase();
    return !!email && this.deleteConfirmation().trim().toLowerCase() === email;
  });

  protected readonly addPasskeyUrl = `/add-passkey?returnUrl=${encodeURIComponent(ACCOUNT_ROUTE)}`;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopResendCooldown());

    // Browser-only: the session cookie never reaches the SSR render, so asking there would
    // paint a signed-out page for a signed-in user and then correct itself on hydration.
    // sessionStorage is browser-only for the same structural reason.
    afterNextRender(() => {
      this.consumeEnrolmentSnapshot();
      void this.reload();
    });
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const view = await this.api.load();
      this.view.set(view);
      // An email change still waiting on its code — started here before a refresh, or in another
      // tab — puts the page back on the code box rather than offering to start a second one.
      // The resend cooldown is not restored with it: only the send that this page made knows when
      // it happened, and guessing from the code's own deadline would hard-code the server's TTL
      // here. The resend is offered, and the endpoint says so if it is still too soon.
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
    this.newEmail.set('');
    this.emailCode.set('');
    this.emailError.set('');
    this.emailStep.set('address');
  }

  /**
   * Asks the BFF to mail a code to the typed address. Nothing about the account changes here —
   * this is the step that proves the mailbox before anything is written to it.
   */
  protected async sendEmailCode(): Promise<void> {
    const target = this.newEmail().trim();
    if (!this.newEmailValid() || this.emailBusy()) {
      return;
    }

    this.emailBusy.set(true);
    this.emailError.set('');
    try {
      await this.api.requestEmailChange(target);
      this.emailCode.set('');
      this.emailStep.set('code');
      this.startResendCooldown();
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

  /** Digits only, six at most, and the invisible field kept in step with the boxes over it. */
  protected setEmailCode(field: HTMLInputElement): void {
    const digits = field.value.replace(/\D/g, '').slice(0, 6);
    field.value = digits;
    this.emailCode.set(digits);
  }

  protected async confirmEmailChange(): Promise<void> {
    const code = this.emailCode();
    if (code.length !== 6 || this.emailBusy()) {
      return;
    }

    this.emailBusy.set(true);
    this.emailError.set('');
    try {
      await this.api.confirmEmailChange(code);
      // The address the BFF wrote is the one it mailed, which this page can only learn by asking.
      // Nothing announces the change: the row above now reads the new address, which is the whole
      // of what happened.
      this.view.set(await this.api.load());
      this.stopResendCooldown();
      this.emailStep.set('idle');
      this.emailCode.set('');
      this.newEmail.set('');
    } catch (err) {
      this.emailError.set(message(err));
    } finally {
      this.emailBusy.set(false);
    }
  }

  /**
   * Drops the change. Which is two different acts depending on where it is cancelled from: in the
   * address step nothing has been mailed, so there is nothing parked and closing the form is
   * entirely local — the endpoint would accept the call and do nothing, at the cost of two round
   * trips. Once a code is out there is server state behind it, and that has to be dropped.
   *
   * The step is the test rather than `pendingEmail`, because a send whose follow-up read failed
   * leaves the page on the code step with a view that has not caught up yet — and that parked
   * change still has to be cancelled.
   */
  protected async abandonEmailChange(): Promise<void> {
    if (this.emailStep() === 'address' && !this.view()?.pendingEmail) {
      this.emailError.set('');
      this.resetEmailChange();
      return;
    }

    this.emailBusy.set(true);
    this.emailError.set('');
    try {
      await this.api.cancelEmailChange();
      this.view.set(await this.api.load());
    } catch (err) {
      this.emailError.set(message(err));
    } finally {
      this.emailBusy.set(false);
      this.resetEmailChange();
    }
  }

  private resetEmailChange(): void {
    this.stopResendCooldown();
    this.emailStep.set('idle');
    this.newEmail.set('');
    this.emailCode.set('');
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

  protected async removePasskey(credentialId: string): Promise<void> {
    // Drop the confirmation as the request goes out: the row it belongs to is about to say
    // "Removing", and two answers to the same question would be on screen at once.
    this.confirmingPasskeyId.set(null);
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
    }
  }

  /**
   * Called on the way out to Keycloak. What it writes is read back by `consumeEnrolmentSnapshot`
   * when the redirect lands here again — sessionStorage because the trip leaves the document, and
   * because the answer is worthless to any other tab or any later visit.
   */
  protected rememberPasskeysBeforeEnrolment(): void {
    try {
      const ids = (this.view()?.passkeys ?? []).map((passkey) => passkey.id);
      sessionStorage.setItem(ENROLMENT_SNAPSHOT_KEY, JSON.stringify(ids));
    } catch {
      // Storage refused (private mode, quota). The badge is decoration; the page is not.
    }
  }

  /** Reads that snapshot exactly once, so the badge lasts this visit and not the next. */
  private consumeEnrolmentSnapshot(): void {
    try {
      const raw = sessionStorage.getItem(ENROLMENT_SNAPSHOT_KEY);
      sessionStorage.removeItem(ENROLMENT_SNAPSHOT_KEY);
      if (raw) {
        this.knownBeforeEnrolment.set(new Set(JSON.parse(raw) as string[]));
      }
    } catch {
      // Unreadable or not JSON — no snapshot, so nothing is flagged.
    }
  }

  protected cancelDelete(): void {
    this.deleteStep.set('form');
    this.deleteConfirmation.set('');
  }

  protected async deleteAccount(): Promise<void> {
    this.deleting.set(true);
    this.error.set('');
    try {
      await this.api.deleteAccount();
      // The account is gone and the endpoint dropped the session with it, so this page is already
      // signed out where it stands: swap it for the farewell and leave. A full-page navigation,
      // not a router link — it drops the in-memory app state along with the session — and
      // `replace`, so Back cannot return to an account page with no account behind it.
      this.deleted.set(true);
      window.location.replace('/');
    } catch (err) {
      this.error.set(message(err));
      // Only on failure: after a delete the page is on its way out and the button goes with it.
      this.deleting.set(false);
    }
  }
}

/** Matches `PendingEmailChange.RequestCooldown` on auth-svc — the countdown is only a courtesy. */
const RESEND_COOLDOWN_SECONDS = 60;

const ENROLMENT_SNAPSHOT_KEY = 'protofast.account.passkeys-before-enrolment';

function message(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
