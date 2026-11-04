import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  Injector,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { messageOf } from '../../loop/errors';
import { companyFindings, Review } from '../../loop/findings';
import { LoopApi } from '../../loop/loop-api';
import { LoopRuns } from '../../loop/loop-runs';
import { LoopStore } from '../../loop/loop-store';
import { LoopTray } from '../../loop/loop-tray';
import { isActive, LoopRun, RunState } from '../../loop/model/loop-run';
import { Icon } from '../../shared/icon';
import { ResearchReview } from './research-review';
import { RunProgress } from './run-progress';

interface TrayRow {
  run: LoopRun;
  kind: 'active' | 'done' | 'failed';
  status: string;
  /** What it works on is still in the loop. */
  openable: boolean;
}

/** The research the tray has grown to show. */
interface Focus {
  run: LoopRun;
  company: string;
  review: Review;
}

type View = 'closed' | 'list' | 'max';

const GROW_MS = 340;
const SHRINK_MS = 200;
const EASE = 'cubic-bezier(0.2, 0.7, 0.2, 1)';

/**
 * The AI's work, pinned bottom-right: a pill that sums up what's researching, reading or writing in
 * the background, opening into a row for each. Research shows its progress here, and when it's done
 * the tray grows to its findings for review.
 */
@Component({
  selector: 'app-run-tray',
  imports: [Icon, ResearchReview, RunProgress],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .tray {
      --tray-room: 96px;
      position: fixed;
      z-index: 30;
      right: 24px;
      bottom: 24px;
      display: flex;
      flex-direction: column;
      align-items: flex-end;
      gap: 8px;
      max-width: calc(100vw - 32px);
    }
    @media (max-width: 719px) {
      .tray {
        --tray-room: 148px;
        right: 16px;
        bottom: 84px;
      }
    }
    .pill {
      min-height: 40px;
      display: inline-flex;
      align-items: center;
      gap: 9px;
      padding: 0 16px;
      border-radius: 20px;
      border: 1px solid var(--color-edge);
      background: var(--color-raise);
      color: var(--color-text-2);
      font-family: var(--font-mono);
      font-size: 12.5px;
      cursor: pointer;
      box-shadow: 0 8px 28px rgb(0 0 0 / 0.45);
    }
    .pill:hover {
      border-color: var(--color-dim);
      color: var(--color-text);
    }
    .panel {
      width: 380px;
      max-width: 100%;
      max-height: min(60vh, 520px);
      display: flex;
      flex-direction: column;
      border: 1px solid var(--color-edge);
      border-radius: 14px;
      background: var(--color-panel);
      box-shadow: 0 14px 40px rgb(0 0 0 / 0.55);
      overflow: hidden;
    }
    .panel.is-max {
      width: 760px;
      max-height: calc(100dvh - var(--tray-room));
      border-radius: 16px;
    }
    .content {
      flex: 1;
      min-height: 0;
      display: flex;
      flex-direction: column;
    }
    .head {
      flex: none;
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px 14px 10px 16px;
      border-bottom: 1px solid var(--color-line-2);
    }
    .is-max .head {
      padding: 10px 12px 10px 24px;
    }
    .heading {
      margin: 0;
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-family: var(--font-mono);
      font-size: 12px;
      font-weight: 400;
      color: var(--color-text-2);
    }
    .heading:focus {
      outline: none;
    }
    .rows {
      flex: 1;
      min-height: 0;
      overflow-y: auto;
    }
    .row {
      display: flex;
      align-items: flex-start;
      gap: 10px;
      padding: 12px 14px 12px 16px;
      border-bottom: 1px solid var(--color-line);
    }
    .label {
      font-size: 14px;
      color: var(--color-text);
    }
    .status {
      display: inline-flex;
      align-items: center;
      gap: 7px;
      font-family: var(--font-mono);
      font-size: 12px;
      color: var(--color-dim);
    }
    .status.is-done {
      color: var(--color-accent);
    }
    .status.is-failed {
      color: var(--color-amber);
    }
    .dot {
      width: 7px;
      height: 7px;
      border-radius: 4px;
      background: currentColor;
    }
    .note {
      font-size: 12.5px;
      color: var(--color-muted);
    }
    .x {
      flex: none;
      width: 28px;
      height: 28px;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      border: 0;
      border-radius: 14px;
      background: none;
      color: var(--color-faint);
      font-size: 17px;
      line-height: 1;
      cursor: pointer;
    }
    .x:hover {
      color: var(--color-text);
      background: var(--color-line-2);
    }
    .is-max .x {
      width: 34px;
      height: 34px;
      border-radius: 17px;
    }
    .foot {
      flex: none;
      display: flex;
      justify-content: flex-end;
      padding: 8px 14px;
    }
    .review-row {
      display: inline-flex;
      align-items: center;
      gap: 6px;
    }
    .review-row .spinner {
      width: 10px;
      height: 10px;
    }
  `,
  template: `
    @if (rows().length > 0) {
      <div class="tray">
        @if (view() !== 'closed') {
          <section
            #panel
            class="panel"
            [class.is-max]="view() === 'max'"
            [attr.aria-label]="view() === 'max' ? 'Research findings' : 'AI work'"
            (keydown.escape)="escape()"
          >
            <div class="content">
              @if (view() === 'max' && focus(); as f) {
                <div class="head">
                  <h3 #maxHeading class="heading" tabindex="-1">research · {{ f.company }}</h3>
                  <button type="button" class="btn-link" (click)="openCompany(f.run)">
                    company page
                  </button>
                  <button type="button" class="x" aria-label="Minimise" (click)="minimise()">
                    <app-icon name="down" [size]="15" />
                  </button>
                  <button type="button" class="x" aria-label="Close" (click)="close()">
                    <app-icon name="close" [size]="14" />
                  </button>
                </div>
                <app-research-review
                  [run]="f.run"
                  [review]="f.review"
                  (reviewChange)="keepTicks($event)"
                  (done)="reviewed(f.run)"
                />
              } @else {
                <div class="head">
                  <h3 class="caption flex-1">ai work</h3>
                  <span class="meta">{{ countText() }}</span>
                  <button type="button" class="x" aria-label="Minimise" (click)="close()">
                    <app-icon name="down" [size]="15" />
                  </button>
                </div>
                <div class="rows">
                  @for (row of rows(); track row.run.id) {
                    <div class="row">
                      <div class="flex min-w-0 flex-1 flex-col gap-1">
                        <span class="label truncate">{{ row.run.label }}</span>
                        @if (row.kind === 'active' && showsSteps(row.run)) {
                          <app-run-progress [run]="row.run" />
                        } @else {
                          <span class="status" [class]="'is-' + row.kind">
                            @if (row.kind === 'active') {
                              <span class="spinner h-2.5 w-2.5" aria-hidden="true"></span>
                            } @else {
                              <span class="dot"></span>
                            }
                            {{ row.status }}
                          </span>
                        }
                        @if (row.kind === 'failed' && row.run.error) {
                          <span class="note">{{ row.run.error }}</span>
                        }
                        @if (row.openable) {
                          @if (row.run.kind === 'researchCompany') {
                            @if (row.kind === 'done') {
                              <button
                                type="button"
                                class="btn-link review-row self-start"
                                [disabled]="loading() === row.run.id"
                                (click)="review(row.run.id, true)"
                              >
                                @if (loading() === row.run.id) {
                                  <span class="spinner" aria-hidden="true"></span>
                                }
                                review it
                              </button>
                            } @else if (row.kind === 'failed') {
                              <button
                                type="button"
                                class="btn-link self-start"
                                (click)="again(row.run)"
                              >
                                try again
                              </button>
                            }
                          } @else {
                            <button
                              type="button"
                              class="btn-link self-start"
                              (click)="open(row.run)"
                            >
                              {{ row.kind === 'done' ? 'review it' : 'open' }}
                            </button>
                          }
                        }
                      </div>
                      @if (row.kind === 'active') {
                        <button type="button" class="x" aria-label="Stop" (click)="cancel(row.run)">
                          ×
                        </button>
                      } @else {
                        <button
                          type="button"
                          class="x"
                          aria-label="Dismiss"
                          (click)="dismiss(row.run)"
                        >
                          ×
                        </button>
                      }
                    </div>
                  }
                </div>
                @if (error()) {
                  <p class="alert m-3" role="alert">{{ error() }}</p>
                }
                @if (finished() > 0) {
                  <div class="foot">
                    <button type="button" class="btn-link" (click)="clearFinished()">
                      clear finished
                    </button>
                  </div>
                }
              }
            </div>
          </section>
        }
        <button
          #pill
          type="button"
          class="pill"
          [attr.aria-expanded]="view() !== 'closed'"
          (click)="togglePill()"
        >
          @if (active() > 0) {
            <span class="spinner h-3 w-3" aria-hidden="true"></span>
          } @else {
            <span class="status" [class]="failed() > 0 ? 'is-failed' : 'is-done'"
              ><span class="dot"></span
            ></span>
          }
          <span>{{ summary() }}</span>
        </button>
      </div>
    }
  `,
})
export class RunTray {
  private readonly api = inject(LoopApi);
  private readonly runs = inject(LoopRuns);
  private readonly store = inject(LoopStore);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  private readonly pill = viewChild<ElementRef<HTMLElement>>('pill');
  private readonly maxHeading = viewChild<ElementRef<HTMLElement>>('maxHeading');

  protected readonly view = signal<View>('closed');
  protected readonly focus = signal<Focus | null>(null);
  /** The run whose findings are on their way. */
  protected readonly loading = signal<string | null>(null);
  protected readonly error = signal('');
  private motion: Animation | null = null;
  /** Bumped by every change of view, so a shrink that's overtaken doesn't close what replaced it. */
  private moves = 0;
  private seen = new Map<string, RunState>();

  private readonly all = computed(() => this.runs.runs());
  protected readonly active = computed(() => this.all().filter(isActive).length);
  private readonly running = computed(() => this.all().filter((r) => r.state === 'running').length);
  private readonly done = computed(() => this.all().filter((r) => r.state === 'succeeded').length);
  protected readonly failed = computed(
    () => this.all().filter((r) => r.state === 'failed' || r.state === 'cancelled').length,
  );
  protected readonly finished = computed(() => this.all().length - this.active());

  protected readonly summary = computed(() => {
    const running = this.running();
    const waiting = this.active() - running;
    if (running || waiting) {
      return [running && `working on ${running}`, waiting && `${waiting} waiting`]
        .filter(Boolean)
        .join(' · ');
    }
    const done = this.done();
    return done ? `${done} to review` : `${this.failed()} stopped`;
  });

  protected readonly countText = computed(() =>
    [
      this.active() && `${this.active()} going`,
      this.done() && `${this.done()} to review`,
      this.failed() && `${this.failed()} stopped`,
    ]
      .filter(Boolean)
      .join(' · '),
  );

  /** Newest first. */
  protected readonly rows = computed<TrayRow[]>(() =>
    [...this.all()].reverse().map((run) => {
      const openable = this.openable(run);
      switch (run.state) {
        case 'queued':
          return { run, openable, kind: 'active', status: 'waiting to start' };
        case 'running':
          return { run, openable, kind: 'active', status: this.verbOf(run) };
        case 'succeeded':
          return { run, openable, kind: 'done', status: 'done, waiting for you' };
        case 'failed':
          return { run, openable, kind: 'failed', status: 'stopped' };
        case 'cancelled':
          return { run, openable, kind: 'failed', status: 'cancelled' };
      }
    }),
  );

  constructor() {
    inject(LoopTray)
      .asks.pipe(takeUntilDestroyed())
      .subscribe((ask) => {
        if ('review' in ask) {
          void this.review(ask.review, true);
        } else if (this.view() === 'closed') {
          this.morph(() => this.view.set('list'));
        }
      });

    // Research seen finishing grows the tray to its findings, unless it already shows others.
    effect(() => {
      const runs = this.all();
      untracked(() => {
        const ended = runs.find(
          (run) =>
            run.kind === 'researchCompany' &&
            run.state === 'succeeded' &&
            (this.seen.get(run.id) === 'queued' || this.seen.get(run.id) === 'running') &&
            this.openable(run),
        );
        this.seen = new Map(runs.map((run) => [run.id, run.state]));
        const focused = this.focus();
        if (focused && !runs.some((run) => run.id === focused.run.id)) {
          this.focus.set(null);
          if (this.view() === 'max') {
            this.view.set(runs.length ? 'list' : 'closed');
          }
        }
        if (ended && this.view() !== 'max') {
          void this.review(ended.id, false);
        }
      });
    });
  }

  protected togglePill(): void {
    if (this.view() === 'closed') {
      this.morph(() => this.view.set('list'));
    } else {
      void this.close();
    }
  }

  protected escape(): void {
    if (this.view() === 'max') {
      this.minimise();
    } else {
      void this.close();
    }
  }

  protected minimise(): void {
    this.morph(() => this.view.set('list'));
  }

  /** Shrinks the panel into the pill. */
  protected async close(): Promise<void> {
    const move = ++this.moves;
    const held = this.panel()?.nativeElement.contains(document.activeElement);
    await this.shrink();
    if (move === this.moves) {
      this.view.set('closed');
      if (held) {
        this.pill()?.nativeElement.focus();
      }
    }
  }

  /** Grows the tray to a research run's findings, kept as they were ticked if it showed them before. */
  protected async review(runId: string, asked: boolean): Promise<void> {
    const run = this.all().find((r) => r.id === runId);
    const company = run?.companyId ? this.store.companiesById().get(run.companyId) : undefined;
    if (!run || run.state !== 'succeeded' || !company || this.loading()) {
      return;
    }
    let review = this.focus()?.run.id === run.id ? this.focus()!.review : null;
    if (!review) {
      this.loading.set(run.id);
      this.error.set('');
      try {
        const { result } = await this.api.getRun(run.id);
        if (result?.kind !== 'company') {
          throw new Error('no findings');
        }
        review = {
          findings: companyFindings(company, result.findings.company),
          sources: result.findings.sources,
        };
      } catch (err) {
        this.error.set(messageOf(err, 'Its findings couldn’t be loaded.'));
        return;
      } finally {
        this.loading.set(null);
      }
    }
    const shown = review;
    this.morph(
      () => {
        this.focus.set({ run, company: company.name, review: shown });
        this.view.set('max');
      },
      () => {
        if (asked) {
          this.maxHeading()?.nativeElement.focus();
        }
      },
    );
  }

  protected keepTicks(review: Review): void {
    this.focus.update((focus) => (focus ? { ...focus, review } : focus));
  }

  /** Saved or discarded: the run goes, and the tray goes back to the rest of the work. */
  protected async reviewed(run: LoopRun): Promise<void> {
    const dismiss = () => {
      this.focus.set(null);
      this.runs.dismiss([run.id]).catch(() => undefined);
    };
    if (this.rows().length > 1) {
      this.morph(() => {
        dismiss();
        this.view.set('list');
      });
      return;
    }
    const move = ++this.moves;
    await this.shrink();
    if (move === this.moves) {
      this.view.set('closed');
      dismiss();
    }
  }

  protected openCompany(run: LoopRun): void {
    void this.router.navigate(['/app/loop'], { queryParams: { id: run.companyId } });
  }

  protected again(run: LoopRun): void {
    const company = run.companyId ? this.store.companiesById().get(run.companyId) : undefined;
    if (!company) {
      return;
    }
    this.error.set('');
    this.runs
      .start(`Research · ${company.name}`, { kind: 'researchCompany', companyId: company.id })
      .catch((err) => this.error.set(messageOf(err, 'The research couldn’t start.')));
  }

  protected cancel(run: LoopRun): void {
    this.runs.cancel(run.id).catch(() => undefined);
  }

  protected dismiss(run: LoopRun): void {
    this.runs.dismiss([run.id]).catch(() => undefined);
  }

  protected clearFinished(): void {
    this.runs.clearFinished().catch(() => undefined);
  }

  protected readonly showsSteps = (run: LoopRun) =>
    run.kind === 'researchCompany' || run.kind === 'readResume' || run.kind === 'tailorResume';

  protected open(run: LoopRun): void {
    void this.close();
    if (run.kind === 'write') {
      const note = this.store.notes().find((n) => n.id === run.noteId);
      void (note?.companyId
        ? this.router.navigate(['/app/loop'], {
            queryParams: { id: note.companyId, note: note.id },
          })
        : note?.kind === 'story'
          ? this.router.navigate(['/app/resume'], { queryParams: { view: 'stories', id: note.id } })
          : this.router.navigate(['/app/notes'], { queryParams: { id: run.noteId } }));
    } else if (run.kind === 'readResume') {
      void this.router.navigate(['/app/resume']);
    } else if (run.kind === 'tailorResume') {
      void this.router.navigate(['/app/resume'], { queryParams: { for: run.applicationId } });
    } else if (run.companyId) {
      void this.router.navigate(['/app/loop'], {
        queryParams: { id: run.companyId, application: run.applicationId ?? undefined },
      });
    }
  }

  /** Changes what the panel shows, then grows or shrinks it from the size it was to its new one. */
  private morph(change: () => void, after?: () => void): void {
    ++this.moves;
    const from = (this.panel() ?? this.pill())?.nativeElement.getBoundingClientRect();
    this.motion?.cancel();
    this.motion = null;
    change();
    afterNextRender(
      () => {
        const panel = this.panel()?.nativeElement;
        if (panel) {
          this.motion = grow(panel, from);
        }
        after?.();
      },
      { injector: this.injector },
    );
  }

  private async shrink(): Promise<void> {
    const panel = this.panel()?.nativeElement;
    const pill = this.pill()?.nativeElement;
    if (!panel || !pill) {
      return;
    }
    const from = panel.getBoundingClientRect();
    const to = pill.getBoundingClientRect();
    const duration = still() ? 100 : SHRINK_MS;
    this.motion?.cancel();
    const motion = panel.animate(
      still()
        ? [{ opacity: 1 }, { opacity: 0 }]
        : [
            { width: `${from.width}px`, height: `${from.height}px`, opacity: 1 },
            { width: `${to.width}px`, height: `${to.height}px`, opacity: 0 },
          ],
      { duration, easing: 'cubic-bezier(0.4, 0, 1, 1)', fill: 'forwards' },
    );
    this.motion = motion;
    // A hidden tab's animations don't advance, so the timer makes sure the tray still closes.
    await Promise.race([
      motion.finished.catch(() => undefined),
      new Promise((resolve) => setTimeout(resolve, duration + 50)),
    ]);
  }

  private verbOf(run: LoopRun): string {
    switch (run.kind) {
      case 'researchCompany':
        return 'researching';
      case 'readPosting':
        return 'reading';
      case 'write':
        return 'writing';
      case 'readResume':
        return 'reading';
      case 'tailorResume':
        return 'tailoring';
    }
  }

  private openable(run: LoopRun): boolean {
    switch (run.kind) {
      case 'write':
        return this.store.notes().some((n) => n.id === run.noteId);
      case 'readResume':
        return true;
      case 'tailorResume':
        return !!run.applicationId && this.store.applicationsById().has(run.applicationId);
      default:
        return !!run.companyId && this.store.companiesById().has(run.companyId);
    }
  }
}

function still(): boolean {
  return matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/** The panel's box eases between the two sizes while what's in it fades up. */
function grow(panel: HTMLElement, from: DOMRect | undefined): Animation | null {
  const content = panel.firstElementChild as HTMLElement | null;
  if (still() || !from) {
    content?.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 140 });
    return null;
  }
  const to = panel.getBoundingClientRect();
  if (Math.abs(from.width - to.width) < 1 && Math.abs(from.height - to.height) < 1) {
    return null;
  }
  content?.animate([{ opacity: 0 }, { opacity: 0, offset: 0.35 }, { opacity: 1 }], {
    duration: GROW_MS,
    easing: 'ease-out',
  });
  return panel.animate(
    [
      { width: `${from.width}px`, height: `${from.height}px` },
      { width: `${to.width}px`, height: `${to.height}px` },
    ],
    { duration: GROW_MS, easing: EASE },
  );
}
