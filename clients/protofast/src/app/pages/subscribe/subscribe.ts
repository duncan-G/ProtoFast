import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ProtofastLogo } from '../../shared/protofast-logo';

/**
 * Where the workflow has got to. `confirming` is the one that matters: the payment
 * provider hands the browser back before its webhook has reached us, so there is a
 * window in which the user has genuinely paid and the account still says otherwise.
 * Bouncing them back into checkout there would charge them twice.
 */
type Stage = 'checkout' | 'confirming' | 'passkey';

/** How long to keep saying "confirming" before admitting something is wrong. */
const CONFIRMATION_GRACE_MS = 20_000;

/**
 * The subscription workflow, and the doorway to a passkey.
 *
 * Two things about the ending are deliberate. It stops on a screen with a button rather
 * than redirecting on its own — enrolling a passkey is a browser ceremony that needs a
 * gesture, and a silent redirect into one reads as a hijack. And the button is a
 * full-page navigation to a BFF endpoint, not a router link: `/add-passkey` is served by
 * auth-svc, which starts the Keycloak round trip that performs the enrolment. Angular
 * decides *when* to send the user; Keycloak does the work.
 */
@Component({
  selector: 'app-subscribe',
  imports: [ProtofastLogo],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="nocturne flex min-h-screen flex-col antialiased">
      <header class="border-b border-[var(--color-divider)]">
        <nav class="mx-auto flex w-full max-w-[1080px] items-center px-6 py-4 sm:px-10">
          <app-protofast-logo />
        </nav>
      </header>

      <main class="mx-auto flex w-full max-w-[560px] flex-1 flex-col justify-center px-6 py-16">
        @switch (stage()) {
          @case ('checkout') {
            <h1 class="text-[28px] leading-tight">Pick a plan</h1>
            <p class="mt-3 text-muted">
              Your account is ready. Choose a plan to start building.
            </p>

            <div class="card mt-8">
              <p class="card-kicker">Prototype</p>
              <p class="card-title">Everything, one project at a time</p>
              <p class="card-body">Thirty minutes from prompt to running product.</p>
            </div>

            <button type="button" class="btn btn-primary btn-block mt-8" (click)="startCheckout()">
              Continue to payment
            </button>
          }

          @case ('confirming') {
            <h1 class="text-[28px] leading-tight">Confirming your payment</h1>
            <p class="mt-3 text-muted">
              This usually takes a few seconds. You can leave this page — nothing is lost.
            </p>

            @if (confirmationSlow()) {
              <p class="mt-6 text-muted">
                It's taking longer than usual. Your payment went through; the account will
                catch up. If it hasn't in a few minutes,
                <a href="mailto:support&#64;protofast.dev">let us know</a>.
              </p>
            }

            <button type="button" class="btn btn-secondary btn-block mt-8" (click)="confirmed()">
              Continue
            </button>
          }

          @case ('passkey') {
            <h1 class="text-[28px] leading-tight">Add a passkey</h1>
            <p class="mt-3 text-muted">
              Sign in with your face, fingerprint or device PIN instead of waiting for a
              code every time. It takes a few seconds and you can add one later instead.
            </p>

            <!-- /add-passkey is a BFF endpoint, not an Angular route — full-page navigation. -->
            <a [href]="addPasskeyUrl()" rel="external" class="btn btn-primary btn-block mt-8">
              Add a passkey
            </a>
            <a [href]="returnUrl()" rel="external" class="btn btn-ghost btn-block mt-3">
              Not now
            </a>
          }
        }
      </main>
    </div>
  `,
})
export class Subscribe {
  private readonly route = inject(ActivatedRoute);

  protected readonly stage = signal<Stage>('checkout');
  protected readonly confirmationSlow = signal(false);

  /** Where the user was heading before the callback diverted them here. */
  protected readonly returnUrl = computed(() => {
    const raw = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/app';
    // Same rule the BFF applies: same-origin paths only, never a protocol-relative URL.
    return raw.startsWith('/') && !raw.startsWith('//') ? raw : '/app';
  });

  protected readonly addPasskeyUrl = computed(
    () => `/add-passkey?returnUrl=${encodeURIComponent(this.returnUrl())}`,
  );

  protected startCheckout(): void {
    // The payment provider redirect goes here once billing lands. Until then the
    // workflow still has to end where it will end for real — on the passkey doorway.
    this.stage.set('confirming');
    setTimeout(() => this.confirmationSlow.set(true), CONFIRMATION_GRACE_MS);
  }

  protected confirmed(): void {
    this.stage.set('passkey');
  }
}
