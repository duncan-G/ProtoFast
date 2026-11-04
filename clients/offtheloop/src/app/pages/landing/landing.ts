import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthIdentityService } from '../../auth/auth-identity';
import { Status } from '../../loop/model/status';
import { Icon } from '../../shared/icon';
import { LoopLogo } from '../../shared/loop-logo';
import { StageDot } from '../../shared/stage-dot';

interface PreviewDay {
  date: number;
  kind: 'today' | 'round' | 'offer' | null;
  label: string;
}

const PREVIEW_DAYS: PreviewDay[] = [
  { date: 3, kind: 'today', label: '' },
  { date: 4, kind: 'round', label: 'screen' },
  { date: 5, kind: 'round', label: 'tech' },
  { date: 6, kind: 'round', label: 'onsite' },
  ...[7, 8, 9, 10, 11].map((date): PreviewDay => ({ date, kind: null, label: '' })),
  { date: 12, kind: 'offer', label: 'offer' },
];

const PREVIEW_ROWS: { company: string; stage: string; status: Status }[] = [
  { company: 'Corvid Games', stage: 'offer', status: 'offer' },
  { company: 'Halcyon Pay', stage: 'onsite', status: 'active' },
  { company: 'Northwind Labs', stage: 'tech', status: 'active' },
  { company: 'Brightfield', stage: 'applied', status: 'applied' },
  { company: 'Lumen Health', stage: 'closed', status: 'closed' },
];

@Component({
  selector: 'app-landing',
  imports: [Icon, LoopLogo, RouterLink, StageDot],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto flex max-w-[1160px] flex-col px-[clamp(16px,4vw,48px)] text-[16px]">
      <header class="flex min-h-[80px] flex-wrap items-center justify-between gap-3">
        <a routerLink="/" aria-label="OffTheLoop home" class="no-underline"
          ><app-loop-logo [wordmark]="true"
        /></a>
        <nav aria-label="Account" class="flex items-center gap-2">
          @if (signedIn) {
            <a routerLink="/app" class="btn btn-primary">Open the app</a>
          } @else {
            <a
              href="/signin"
              rel="external"
              class="btn border-0 text-[15px] text-[var(--color-muted)]"
              >Sign in</a
            >
            <a href="/signup" rel="external" class="btn btn-primary text-[15px]">Get started</a>
          }
        </nav>
      </header>

      <section
        class="flex flex-wrap items-center gap-x-[72px] gap-y-14 pt-[clamp(48px,8vw,112px)] pb-[clamp(64px,9vw,128px)]"
      >
        <div class="flex min-w-0 flex-[5_1_380px] flex-col gap-6">
          <p class="mono m-0 text-[13px] text-[var(--color-dim)]">
            // job-search tracker for engineers
          </p>
          <h1
            class="m-0 text-[clamp(44px,6vw,76px)] leading-[0.98] font-semibold tracking-[-0.055em]"
          >
            Get off the loop.<br /><span class="text-[var(--color-accent)]">Get the offer.</span>
          </h1>
          <p class="m-0 max-w-[44ch] text-[18px] text-balance text-[var(--color-muted)]">
            <span class="block">Track every application and interview round.</span>
            <span class="block">Keep your prep notes one search away.</span>
          </p>
          <div class="mt-2 flex flex-wrap gap-2.5">
            <a
              [href]="signedIn ? '/app' : '/signup'"
              rel="external"
              class="btn btn-primary min-h-[48px] px-[22px] text-[16px]"
              >Get started</a
            >
            <a routerLink="/app" class="btn min-h-[48px] px-[22px] text-[16px] font-normal"
              >See the app</a
            >
          </div>
        </div>

        <div
          role="img"
          aria-label="Preview: the next ten days with three interviews and an offer deadline, and five companies by stage"
          class="flex min-w-0 flex-[6_1_440px] flex-col gap-7 rounded-[20px] bg-[var(--color-panel)] px-7 pt-7 pb-5 max-[479px]:px-4"
        >
          <div class="flex flex-col gap-1.5">
            <p class="label m-0">next 10 days</p>
            <p class="m-0 text-[22px] font-semibold tracking-[-0.04em]">
              3 rounds<span class="text-[var(--color-faint)]"> · </span
              ><span class="text-[var(--color-accent)]">1 offer</span>
            </p>
          </div>
          <div class="grid grid-cols-10">
            @for (day of days; track day.date) {
              <span class="flex flex-col items-center gap-2">
                <span
                  class="mono text-[11px]"
                  [style.color]="day.kind === 'today' ? 'var(--color-accent)' : 'var(--color-dim)'"
                  >{{ day.date }}</span
                >
                <span
                  class="flex h-4 items-center justify-center self-stretch bg-[linear-gradient(var(--color-line-2),var(--color-line-2))] bg-[length:100%_1px] bg-center bg-no-repeat"
                >
                  <span class="block box-border rounded-full" [style]="dotStyle(day)"></span>
                </span>
                <span
                  class="mono min-h-[15px] text-[10px] max-[479px]:hidden"
                  [style.color]="day.kind === 'offer' ? 'var(--color-accent)' : 'var(--color-dim)'"
                  >{{ day.label }}</span
                >
              </span>
            }
          </div>
          <div class="flex flex-col">
            <p class="label mt-0 mb-2.5 flex justify-between">
              <span>applications</span><span>stage</span>
            </p>
            @for (row of rows; track row.company) {
              <span
                class="flex min-h-[38px] items-center gap-3 border-t border-[var(--color-line)] text-[14px]"
                [style.color]="row.status === 'closed' ? 'var(--color-dim)' : null"
              >
                <app-stage-dot [status]="row.status" />
                <span class="flex-1">{{ row.company }}</span>
                <span class="mono text-[11.5px] text-[var(--color-dim)]">{{ row.stage }}</span>
              </span>
            }
          </div>
        </div>
      </section>

      <section
        aria-label="How it works"
        class="grid grid-cols-[repeat(auto-fit,minmax(260px,1fr))] gap-12 border-t border-[var(--color-line)] py-14"
      >
        <div class="flex flex-col gap-[18px]">
          <span class="mono text-[13px] text-[var(--color-accent)]">01 jot</span>
          <span class="flex min-h-[52px] items-center gap-3 text-[17px] font-medium"
            >Monotonic stack
            <span class="mono text-[12px] font-normal text-[var(--color-dim)]">O(n)</span></span
          >
          <p class="m-0 text-[var(--color-muted)]">
            Note a coding pattern, a system design or a story once, in your words.
          </p>
        </div>
        <div class="flex flex-col gap-[18px]">
          <span class="mono text-[13px] text-[var(--color-accent)]">02 write</span>
          <span class="flex min-h-[52px] items-center gap-3 text-[17px] font-medium"
            >Idempotency <span class="h-px flex-[0_1_48px] bg-[var(--color-edge)]"></span
            ><span class="mono text-[13px] font-normal text-[var(--color-dim)]"
              >space · ask AI</span
            ></span
          >
          <p class="m-0 text-[var(--color-muted)]">
            Write it out with code, maths and diagrams, or have AI draft it.
          </p>
        </div>
        <div class="flex flex-col gap-[18px]">
          <span class="mono text-[13px] text-[var(--color-accent)]">03 look up</span>
          <span
            class="flex min-h-[52px] items-center gap-3 border-b border-[var(--color-edge)] text-[17px]"
          >
            <span class="text-[var(--color-dim)]"><app-icon name="search" [size]="16" /></span>
            <span class="flex-1">idempotency</span
            ><span class="mono text-[12px] text-[var(--color-dim)]">3</span>
          </span>
          <p class="m-0 text-[var(--color-muted)]">Find it in seconds, right before the call.</p>
        </div>
      </section>

      <section
        aria-labelledby="not-course"
        class="flex flex-wrap items-center gap-x-[72px] gap-y-6 border-t border-[var(--color-line)] py-14"
      >
        <h2
          id="not-course"
          class="m-0 flex-[1_1_260px] text-[clamp(28px,3vw,40px)] font-semibold tracking-[-0.045em]"
        >
          Not a prep course.
        </h2>
        <div class="mono flex min-w-0 flex-[2_1_420px] flex-col gap-2.5 text-[15px]">
          <span class="flex gap-4 text-[var(--color-danger)]"
            ><span aria-hidden="true">-</span>108 problems, a streak to keep</span
          >
          <span class="flex gap-4 text-[var(--color-accent)]"
            ><span aria-hidden="true">+</span>what you already know, one search away</span
          >
        </div>
      </section>

      <section
        class="flex flex-col items-start gap-6 border-t border-[var(--color-line)] py-[clamp(64px,9vw,120px)]"
      >
        <h2 class="m-0 text-[clamp(36px,4.6vw,60px)] leading-none font-semibold tracking-[-0.05em]">
          Make this loop<br />your last.
        </h2>
        <a
          [href]="signedIn ? '/app' : '/signup'"
          rel="external"
          class="btn btn-primary min-h-[48px] px-[22px] text-[16px]"
          >Get started</a
        >
      </section>

      <footer
        class="mono flex flex-wrap justify-between gap-3 border-t border-[var(--color-line)] pt-6 pb-10 text-[12px] text-[var(--color-dim)]"
      >
        <span>process.exit(0);</span>
      </footer>
    </div>
  `,
})
export class Landing {
  protected readonly signedIn = inject(AuthIdentityService).authenticated;
  protected readonly days = PREVIEW_DAYS;
  protected readonly rows = PREVIEW_ROWS;

  protected dotStyle(day: PreviewDay): string {
    switch (day.kind) {
      case 'today':
        return 'width: 8px; height: 8px; border: 2px solid var(--color-accent)';
      case 'round':
        return 'width: 9px; height: 9px; background: var(--color-amber)';
      case 'offer':
        return 'width: 12px; height: 12px; background: var(--color-accent)';
      default:
        return 'width: 4px; height: 4px; background: var(--color-edge)';
    }
  }
}
