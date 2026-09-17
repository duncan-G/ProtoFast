import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthIdentityService } from '../../auth/auth-identity';
import { ThePlotLogo } from '../../shared/theplot-logo';

/**
 * The only public page. It exists to explain what the product does to someone who arrived at
 * theplot.<zone> without an account, and to get a signed-in user into /app in one click.
 */
@Component({
  selector: 'app-landing',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ThePlotLogo],
  template: `
    <div class="nocturne min-h-screen bg-[var(--color-bg)] text-[var(--color-text)] antialiased">
      <header class="border-b border-[var(--color-divider)]">
        <nav class="mx-auto flex max-w-6xl items-center justify-between px-6 py-4">
          <app-theplot-logo class="text-base" />

          @if (auth.authenticated) {
            <a routerLink="/app" class="btn btn-primary">Open your library</a>
          } @else {
            <!-- /signin is a BFF endpoint, not an Angular route — full-page navigation. -->
            <a href="/signin?returnUrl=%2Fapp" rel="external" class="btn btn-primary">Sign in</a>
          }
        </nav>
      </header>

      <main class="mx-auto max-w-4xl px-6 py-24 text-center">
        <h1 class="text-4xl font-semibold tracking-tight sm:text-5xl">
          Documents have structure. Most files have lost it.
        </h1>
        <p class="mx-auto mt-6 max-w-2xl text-lg text-[var(--color-neutral-400)]">
          Upload a document in any condition — clean Markdown, a PDF conversion full of layout
          noise, a flat transcript — and get back a paragraph list with stable identifiers and a
          section tree you can browse, review and build on.
        </p>

        <div class="mt-10 flex justify-center gap-3">
          @if (auth.authenticated) {
            <a routerLink="/app/upload" class="btn btn-primary">Upload a document</a>
            <a routerLink="/app" class="btn btn-secondary">Your library</a>
          } @else {
            <a href="/signin?returnUrl=%2Fapp%2Fupload" rel="external" class="btn btn-primary">
              Get started
            </a>
          }
        </div>

        <dl class="mx-auto mt-24 grid max-w-3xl gap-10 text-left sm:grid-cols-3">
          <div>
            <dt class="text-sm font-medium">Nothing is rewritten</dt>
            <dd class="mt-1.5 text-sm text-[var(--color-neutral-400)]">
              Paragraph text is joined from your document by code, never regenerated. Every result
              is checked character-for-character against the source before it is kept.
            </dd>
          </div>
          <div>
            <dt class="text-sm font-medium">Existing structure is trusted</dt>
            <dd class="mt-1.5 text-sm text-[var(--color-neutral-400)]">
              Headings and paragraph breaks your document already has are treated as facts. A
              well-formed document finishes in seconds without a model being asked anything.
            </dd>
          </div>
          <div>
            <dt class="text-sm font-medium">You can check the work</dt>
            <dd class="mt-1.5 text-sm text-[var(--color-neutral-400)]">
              Every phase leaves a file you can open, inferred section titles are marked as
              inferred, and you can hold a document for your own approval before it is frozen.
            </dd>
          </div>
        </dl>
      </main>
    </div>
  `,
})
export class Landing {
  protected readonly auth = inject(AuthIdentityService);
}
