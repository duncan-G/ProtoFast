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
 * The page is one 700px column. A section is a lettered label, the controls at full width beneath
 * it, and the sentence that explains them beneath *those* — prose as a footnote to a control
 * rather than an introduction to it. The controls sit on `.surface-card` rows; a form opening
 * replaces the row it came from with a `.panel` of the same ground and geometry, so a section
 * reads as one card that grew rather than two cards stacked. Everything that arrives carries
 * `.rise`, and rows in the passkey list stagger, so an addition is seen rather than just found.
 *
 * Still no modals, and the only route change in the page is the passkey hand-off, which has to
 * leave.
 */
@Component({
  selector: 'app-account',
  imports: [AccountMenu, DatePipe, RouterLink, ProtofastLogo],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The wash is the landing page's, dimmed and moved to the top-left corner: enough to keep
         the header off a flat ground, not enough to compete with the accent ring on an open form. -->
    <div
      class="nocturne flex min-h-screen flex-col bg-[radial-gradient(1100px_520px_at_12%_-12%,color-mix(in_srgb,var(--color-accent)_11%,transparent),transparent_62%)] antialiased"
    >
      <header>
        <nav class="mx-auto flex w-full max-w-[700px] items-center px-6 py-[17px] sm:px-8">
          <a routerLink="/app" class="mr-auto no-underline"><app-protofast-logo /></a>
          <!-- Account and sign-out live behind the avatar; see shared/account-menu.ts. -->
          <app-account-menu />
        </nav>
      </header>

      <main class="mx-auto w-full max-w-[700px] flex-1 px-6 pb-[100px] sm:px-8">
        @if (deleted()) {
          <div
            class="surface-card rise mt-[22px] flex flex-col items-start gap-2.5 px-[22px] py-[26px]"
          >
            <h1 class="text-[26px]">Account deleted</h1>
            <p class="m-0 max-w-[380px] text-muted text-[13.5px] leading-[1.55]">
              {{ view()?.email ?? 'Your account' }} is gone, along with everything stored under it.
              You've been signed out on every device.
            </p>
            <!-- The redirect leaves this page at once; the link is the way out if it cannot. -->
            <a href="/" rel="external" class="btn btn-secondary mt-1">Back to protofast.dev</a>
          </div>
        } @else {
          <!-- ──────────────────────── title, and who this is ───────────────────────── -->
          <div class="flex flex-wrap items-end justify-between gap-[17px] pt-[22px] pb-[17px]">
            <div class="min-w-0">
              <p
                class="m-0 mb-2.5 text-[11px] tracking-[0.14em] text-[var(--color-accent)] uppercase"
              >
                Settings
              </p>
              <h1 class="mb-1.5 text-[46px] tracking-[-0.03em]">Account</h1>
              <p class="m-0 text-muted text-[15px]">How you sign in, and how to leave.</p>
            </div>

            <!-- The same address the Email section holds, as identification rather than as a
                 control: it answers "which account am I about to change" before anything is. -->
            <div
              class="flex min-w-0 items-center gap-2.5 rounded-[var(--radius-lg)] bg-[color-mix(in_srgb,var(--color-surface)_70%,transparent)] px-[11px] py-[9px] shadow-[var(--shadow-sm)]"
            >
              <span
                class="grid h-[34px] w-[34px] flex-none place-items-center rounded-full bg-[linear-gradient(150deg,var(--color-accent-600),var(--color-accent-800))] font-[family-name:var(--font-heading)] text-[13px] text-[var(--color-accent-100)]"
                aria-hidden="true"
                >{{ initial() }}</span
              >
              <span class="min-w-0 leading-[1.3]">
                <span class="block truncate text-[13px]">{{ emailLabel() }}</span>
                <span
                  class="block text-[11px] text-[color-mix(in_srgb,var(--color-text)_45%,transparent)]"
                  >{{ signInSummary() }}</span
                >
              </span>
            </div>
          </div>

          <hr class="hr m-0" />

          @if (error()) {
            <p class="panel-danger rise mt-[17px] mb-0 text-[13px]" role="alert">{{ error() }}</p>
          }

          <!-- ───────────────────────────── email ───────────────────────────── -->
          <section class="py-[22px]">
            <h2 class="section-label">Email</h2>

            @if (emailStep() === 'idle') {
              <div class="surface-card surface-row rise flex-wrap">
                <svg
                  class="h-[18px] w-[18px] flex-none text-[var(--color-accent)]"
                  viewBox="0 0 256 256"
                  fill="currentColor"
                  aria-hidden="true"
                >
                  <path
                    d="M224 48H32a8 8 0 0 0-8 8v136a16 16 0 0 0 16 16h176a16 16 0 0 0 16-16V56a8 8 0 0 0-8-8m-96 85.15L52.57 64h150.86ZM98.71 128 40 181.81v-107.6ZM109.19 139l13.39 12.26a8 8 0 0 0 10.84 0L146.81 139l50.29 46.09H58.9Zm48.1-11 58.71-53.79v107.62Z"
                  />
                </svg>
                <div class="mr-auto min-w-0 flex-[1_1_180px]">
                  <p
                    class="m-0 text-[10px] tracking-[0.12em] text-[color-mix(in_srgb,var(--color-text)_45%,transparent)] uppercase"
                  >
                    Signed in as
                  </p>
                  <p class="mt-[3px] mb-0 text-[16px] [overflow-wrap:anywhere]">
                    {{ emailLabel() }}
                  </p>
                </div>
                <button
                  type="button"
                  class="btn btn-secondary"
                  [disabled]="loading()"
                  (click)="startEmailChange()"
                >
                  Change
                </button>
              </div>
            }

            @if (emailStep() === 'address') {
              <div class="panel rise flex flex-col gap-3">
                <div class="field">
                  <label for="new-email">New email address</label>
                  <input
                    id="new-email"
                    class="input max-w-[380px] min-h-[40px]"
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
                  <button
                    type="button"
                    class="btn btn-ghost"
                    [disabled]="emailBusy()"
                    (click)="useDifferentAddress()"
                  >
                    Use a different address
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

            @if (emailError()) {
              <p class="panel-danger rise mt-[8px] mb-0 text-[13px]" role="alert">
                {{ emailError() }}
              </p>
            }

            <p class="section-note">
              The address that reaches your account and receives sign-in codes. A change is
              confirmed at the new address first, so a typo can't lock you out.
            </p>
          </section>

          <hr class="hr m-0" />

          <!-- ──────────────────────────── passkeys ─────────────────────────── -->
          <section class="py-[22px]">
            <h2 class="section-label">Passkeys</h2>

            @if (loading()) {
              <p class="m-0 text-muted text-[13px]">Loading your passkeys…</p>
            } @else if (view()?.passkeysUnavailable) {
              <div class="surface-card surface-row rise flex-wrap">
                <div class="mr-auto min-w-0 flex-[1_1_190px]">
                  <p class="m-0 text-[15px]">Your passkeys couldn't be loaded</p>
                  <p class="m-0 text-muted text-[12px]">
                    Everything else on this page is still current.
                  </p>
                </div>
                <button type="button" class="btn btn-secondary" (click)="reload()">
                  Try again
                </button>
              </div>
            } @else {
              <ul class="m-0 flex list-none flex-col gap-2 p-0">
                @for (passkey of passkeys(); track passkey.id) {
                  <!-- Staggered, and capped at five steps so a long list does not turn its last
                       row into a wait. A tracked row that was already here does not re-enter. -->
                  <li
                    class="surface-card rise overflow-hidden"
                    [style.animation-delay.ms]="riseDelay($index)"
                  >
                    <div class="surface-row flex-wrap">
                      <span
                        class="grid h-8 w-8 flex-none place-items-center rounded-full bg-[color-mix(in_srgb,var(--color-accent)_14%,transparent)] text-[var(--color-accent)]"
                        aria-hidden="true"
                      >
                        <svg class="h-[17px] w-[17px]" viewBox="0 0 256 256" fill="currentColor">
                          <path
                            d="M180 32a76 76 0 0 0-72.31 99.86L36.69 202.9A15.86 15.86 0 0 0 32 214.22V240a8 8 0 0 0 8 8h40a8 8 0 0 0 8-8v-16h16a8 8 0 0 0 8-8v-16h16a8 8 0 0 0 5.66-2.34l14.48-14.48A76 76 0 1 0 180 32m0 24a20 20 0 1 1-20 20a20 20 0 0 1 20-20"
                          />
                        </svg>
                      </span>
                      <div class="mr-auto min-w-0 flex-[1_1_160px]">
                        <p class="m-0 flex flex-wrap items-center gap-2 text-[15px]">
                          <span class="truncate">{{ passkey.label || 'Passkey' }}</span>
                          @if (passkey.isNew) {
                            <span class="tag tag-accent">New</span>
                          }
                        </p>
                        <p
                          class="m-0 text-[12px] text-[color-mix(in_srgb,var(--color-text)_45%,transparent)]"
                        >
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
                          class="btn btn-ghost btn-muted-danger text-[13px]"
                          (click)="confirmingPasskeyId.set(passkey.id)"
                        >
                          Remove
                        </button>
                      }
                    </div>

                    <!-- The confirmation opens under the row it is asking about, not over it. -->
                    @if (confirmingPasskeyId() === passkey.id) {
                      <div
                        class="strip-danger rise flex flex-wrap items-center gap-3 px-[17px] py-3"
                      >
                        <p class="m-0 min-w-0 flex-[1_1_210px] text-[13px] leading-[1.5]">
                          Remove this passkey? Signing in from
                          {{ passkey.label || 'that device' }} will need a mailed code again.
                        </p>
                        <div class="flex gap-2">
                          <button
                            type="button"
                            class="btn btn-danger"
                            (click)="removePasskey(passkey.id)"
                          >
                            Remove passkey
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

                @if (passkeys().length === 0) {
                  <li class="surface-card rise px-[17px] py-[15px] text-muted text-[13px]">
                    No passkeys yet. You'll sign in with a mailed code.
                  </li>
                }
              </ul>

              <!-- /add-passkey is a BFF endpoint, not an Angular route — full-page navigation. -->
              <a
                [href]="addPasskeyUrl"
                rel="external"
                class="btn btn-primary mt-[11px]"
                (click)="rememberPasskeysBeforeEnrolment()"
              >
                <svg
                  class="h-[15px] w-[15px]"
                  viewBox="0 0 256 256"
                  fill="currentColor"
                  aria-hidden="true"
                >
                  <path
                    d="M224 128a8 8 0 0 1-8 8h-80v80a8 8 0 0 1-16 0v-80H40a8 8 0 0 1 0-16h80V40a8 8 0 0 1 16 0v80h80a8 8 0 0 1 8 8"
                  />
                </svg>
                {{ passkeys().length === 0 ? 'Add a passkey' : 'Add another passkey' }}
              </a>
            }

            <p class="section-note">
              Face, fingerprint or device PIN — one per device. Removing them all is safe; a mailed
              code always works.
            </p>
          </section>

          <hr class="hr m-0" />

          <!-- ────────────────────────────── leaving ────────────────────────── -->
          <section class="py-[22px]">
            <h2 class="section-label">Leaving</h2>

            @if (deleteStep() === 'closed') {
              <div class="rise">
                <button type="button" class="btn btn-danger" (click)="openDelete()">
                  Delete my account…
                </button>
                <p class="section-note max-w-[460px]">
                  Removes your sign-in and everything we hold for you, immediately and for good. No
                  undo, no grace period.
                </p>
              </div>
            } @else {
              <div class="panel-danger rise max-w-[460px]">
                <p
                  class="m-0 mb-1.5 font-[family-name:var(--font-heading)] text-[15px] text-[var(--color-danger-200)]"
                >
                  This is permanent
                </p>
                <p class="m-0 mb-[13px] text-muted text-[13px] leading-[1.6]">
                  Your passkeys, and everything stored under this account, go with it. Type your
                  email address to confirm.
                </p>
                <input
                  id="delete-confirmation"
                  class="input mb-[13px] min-h-[40px]"
                  type="email"
                  autocomplete="off"
                  spellcheck="false"
                  [attr.aria-label]="
                    'Type ' + (view()?.email ?? 'your email address') + ' to confirm'
                  "
                  [placeholder]="view()?.email ?? 'your email address'"
                  [value]="deleteConfirmation()"
                  [disabled]="deleting()"
                  (input)="deleteConfirmation.set($any($event.target).value)"
                />
                <div class="flex flex-wrap items-center gap-2">
                  <button
                    type="button"
                    class="btn btn-danger"
                    [disabled]="!confirmationMatches() || deleting()"
                    (click)="deleteAccount()"
                  >
                    @if (deleting()) {
                      <span class="spinner" aria-hidden="true"></span>
                    }
                    {{ deleting() ? 'Deleting' : 'Delete forever' }}
                  </button>
                  <button
                    type="button"
                    class="btn btn-ghost text-[color-mix(in_srgb,var(--color-text)_60%,transparent)]"
                    [disabled]="deleting()"
                    (click)="cancelDelete()"
                  >
                    Keep my account
                  </button>
                </div>
              </div>
            }
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
   * Deleting still takes two deliberate acts, but the first one is now opening the thing at all:
   * the section offers a single button, and the panel it opens is where the address is typed and
   * the irreversible button lives. The old shape kept a confirmation form permanently expanded
   * under the heading, which put the page's most destructive control on screen at all times and
   * needed a second armed screen to make up for it.
   */
  protected readonly deleteStep = signal<'closed' | 'open'>('closed');

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

  /** The passkey a Remove click has armed, if any — the second tap is the one that acts. */
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

  /** The address, or what stands in for it while the first load is still out. */
  protected readonly emailLabel = computed(() =>
    this.loading() ? '…' : (this.view()?.email ?? 'Unknown'),
  );

  /** The avatar's one letter. Decoration, hence `aria-hidden` where it is rendered. */
  protected readonly initial = computed(() => {
    const email = this.view()?.email ?? '';
    return (email[0] ?? '?').toUpperCase();
  });

  /**
   * How this account signs in, as the identity chip states it. Says nothing rather than guessing
   * when the credential list is the one thing that could not be read.
   */
  protected readonly signInSummary = computed(() => {
    const view = this.view();
    if (!view || view.passkeysUnavailable) {
      return 'Signed in';
    }
    return view.passkeys.length > 0 ? 'Signed in · passkey' : 'Signed in · emailed code';
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
   * Deleting is irreversible, so the button in the open panel stays inert until the user has
   * typed the address the account is reached at. Case and stray spaces are not the point of the
   * exercise.
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

  /**
   * How long a list row waits before it rises. The cap is what keeps the stagger a flourish on a
   * first paint rather than a delay on the row a returning user is looking for.
   */
  protected riseDelay(index: number): number {
    return Math.min(index, PASSKEY_STAGGER_CAP) * PASSKEY_STAGGER_MS;
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
    this.emailCode.set('');
    this.emailError.set('');
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
    if (!this.newEmailValid() || this.emailBusy()) {
      return;
    }

    this.emailBusy.set(true);
    this.emailError.set('');
    try {
      const pending = await this.api.requestEmailChange(target);
      this.emailCode.set('');
      this.emailStep.set('code');
      if (pending.sent !== false) {
        this.startResendCooldown();
      }
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
   * Closes the form. A code that has already been mailed stays parked — cancel used to
   * delete it, which left the inbox holding a code nothing would accept, and the send
   * cooldown blocked a replacement. Change email again to type it.
   */
  protected abandonEmailChange(): void {
    this.emailError.set('');
    this.resetEmailChange();
  }

  /** Keeps the mailed code; the next send replaces it if a different address goes out. */
  protected useDifferentAddress(): void {
    this.emailError.set('');
    this.emailCode.set('');
    this.newEmail.set('');
    this.emailStep.set('address');
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

  /** Opens the confirmation panel. Nothing typed into a previous one survives into this one. */
  protected openDelete(): void {
    this.deleteConfirmation.set('');
    this.deleteStep.set('open');
  }

  protected cancelDelete(): void {
    this.deleteStep.set('closed');
    this.deleteConfirmation.set('');
  }

  protected async deleteAccount(): Promise<void> {
    // The button is disabled until the address matches; this is the same guard for a click that
    // arrived some other way (Enter on a stale focus, a synthetic event).
    if (!this.confirmationMatches() || this.deleting()) {
      return;
    }

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

/** The passkey list's entrance stagger: one step per row, and no more than this many steps. */
const PASSKEY_STAGGER_MS = 45;
const PASSKEY_STAGGER_CAP = 5;

const ENROLMENT_SNAPSHOT_KEY = 'protofast.account.passkeys-before-enrolment';

function message(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
