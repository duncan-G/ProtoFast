import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  input,
  OnInit,
  output,
  signal,
} from '@angular/core';
import { messageOf } from '../../loop/errors';
import { Finding, Review } from '../../loop/findings';
import { LoopRuns } from '../../loop/loop-runs';
import { LoopRun } from '../../loop/model/loop-run';
import { ResearchStep, stepSubject } from '../../loop/model/research-step';
import { RunResult } from '../../loop/model/run-result';
import { FindingsList } from './findings-list';

const SHOWN_STEPS = 6;

/**
 * A research run's steps as it goes, then what it found, ticked to keep; nothing saves before. The
 * run goes on when the panel closes, and shows here again when it reopens.
 */
@Component({
  selector: 'app-research-panel',
  imports: [FindingsList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <section class="edit-form edit-panel" [attr.aria-label]="heading()" aria-live="polite">
      <div class="edit-head">
        <h3 class="edit-heading">{{ heading() }}</h3>
        @if (phase() === 'running') {
          <span class="keys">{{ clock() }}</span>
        }
      </div>

      <div class="edit-body">
        @switch (phase()) {
          @case ('running') {
            <ol class="research-steps">
              @for (step of shownSteps(); track $index) {
                <li class="research-step">
                  <span class="research-verb">{{ step.action }}</span>
                  <span class="truncate">{{ stepSubject(step) }}</span>
                </li>
              }
              <li class="research-step research-now">
                <span class="spinner" aria-hidden="true"></span>
                <span>{{ steps().length ? 'working through it' : 'starting' }}</span>
              </li>
            </ol>
          }
          @case ('failed') {
            <p class="alert" role="alert">{{ error() }}</p>
            <ng-content select="[failed]" />
          }
          @case ('review') {
            <app-findings-list [review]="found()!" (reviewChange)="found.set($event)" />
            @if (error()) {
              <p class="alert" role="alert">{{ error() }}</p>
            }
          }
        }
      </div>

      <div class="edit-foot">
        @switch (phase()) {
          @case ('running') {
            <button type="button" class="btn btn-sm" (click)="close()">Stop</button>
          }
          @case ('failed') {
            <button type="button" class="btn btn-sm" (click)="close()">Close</button>
            <button type="button" class="btn btn-sm btn-primary" (click)="again()">
              Try again
            </button>
          }
          @case ('review') {
            @if (found()!.findings.length) {
              <span class="edit-note">{{ kept() }} of {{ found()!.findings.length }} ticked</span>
              <button type="button" class="btn btn-sm" (click)="close()">Discard</button>
              <button
                type="button"
                class="btn btn-sm btn-primary"
                [disabled]="!kept() || busy()"
                (click)="keep()"
              >
                @if (busy()) {
                  <span class="spinner" aria-hidden="true"></span>
                }
                {{ applyLabel() }}
              </button>
            } @else {
              <button type="button" class="btn btn-sm" (click)="close()">Close</button>
            }
          }
        }
      </div>
    </section>
  `,
})
export class ResearchPanel implements OnInit {
  private readonly runs = inject(LoopRuns);

  readonly heading = input.required<string>();
  /** A run already under way or done, to show instead of starting one. */
  readonly existing = input<LoopRun | null>(null);
  readonly start = input.required<() => Promise<LoopRun>>();
  /** Lays what was found out for review. */
  readonly review = input.required<(result: RunResult) => Review>();
  /** Gets the ticked findings. */
  readonly apply = input.required<(findings: Finding[]) => Promise<void>>();
  readonly applyLabel = input('Keep ticked');
  readonly failure = input('The research couldn’t finish.');
  /** After keeping, discarding or stopping. */
  readonly closed = output<void>();

  protected readonly phase = signal<'running' | 'failed' | 'review'>('running');
  protected readonly steps = signal<ResearchStep[]>([]);
  protected readonly found = signal<Review | null>(null);
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  private readonly seconds = signal(0);
  private run: LoopRun | null = null;
  private watching: AbortController | null = null;
  private timer: ReturnType<typeof setInterval> | undefined;

  protected readonly stepSubject = stepSubject;
  protected readonly shownSteps = computed(() => this.steps().slice(-SHOWN_STEPS));
  protected readonly kept = computed(
    () => this.found()?.findings.filter((f) => f.keep).length ?? 0,
  );
  protected readonly clock = computed(() => {
    const s = this.seconds();
    return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  });

  constructor() {
    // Leaving stops the watch; the run carries on and is here again when the panel reopens.
    inject(DestroyRef).onDestroy(() => {
      this.watching?.abort();
      clearInterval(this.timer);
    });
  }

  ngOnInit(): void {
    const existing = this.existing();
    if (existing) {
      void this.show(existing);
    } else {
      void this.again();
    }
  }

  protected async again(): Promise<void> {
    this.watching?.abort();
    this.phase.set('running');
    this.steps.set([]);
    this.error.set('');
    this.tick(new Date());
    try {
      await this.show(await this.start()());
    } catch (err) {
      clearInterval(this.timer);
      this.error.set(messageOf(err, this.failure()));
      this.phase.set('failed');
    }
  }

  /** Stopping, discarding, closing or keeping is done with the run, so it's dismissed. */
  protected close(): void {
    this.watching?.abort();
    if (this.run) {
      this.runs.dismiss([this.run.id]).catch(() => undefined);
    }
    this.closed.emit();
  }

  protected async keep(): Promise<void> {
    const review = this.found();
    if (!review || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.apply()(review.findings.filter((f) => f.keep));
      this.close();
    } catch (err) {
      this.error.set(messageOf(err, 'That could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async show(run: LoopRun): Promise<void> {
    this.run = run;
    const watching = new AbortController();
    this.watching = watching;
    this.phase.set('running');
    this.steps.set([]);
    this.tick(run.started ?? run.created);
    try {
      for await (const update of this.runs.watch(run, watching.signal)) {
        this.steps.update((steps) => [...(update.restarted ? [] : steps), ...update.steps]);
        if (update.result) {
          this.found.set(this.review()(update.result));
        }
        const now = update.run;
        if (now?.state === 'running' && now.started) {
          this.tick(now.started);
        }
        if (now?.state === 'succeeded') {
          this.phase.set(this.found() ? 'review' : 'failed');
          if (!this.found()) {
            this.error.set(this.failure());
          }
        } else if (now?.state === 'failed' || now?.state === 'cancelled') {
          this.error.set(
            now.error || (now.state === 'cancelled' ? 'It was stopped.' : this.failure()),
          );
          this.phase.set('failed');
        }
      }
    } catch (err) {
      if (!watching.signal.aborted) {
        this.error.set(messageOf(err, this.failure()));
        this.phase.set('failed');
      }
    } finally {
      if (this.watching === watching) {
        clearInterval(this.timer);
      }
    }
  }

  /** The clock counts from when the run started, which may be before the panel opened. */
  private tick(from: Date): void {
    clearInterval(this.timer);
    const update = () =>
      this.seconds.set(Math.max(0, Math.floor((Date.now() - from.getTime()) / 1000)));
    update();
    this.timer = setInterval(update, 1000);
  }
}
