import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  input,
  OnInit,
  signal,
} from '@angular/core';
import { LoopRuns } from '../../loop/loop-runs';
import { LoopRun } from '../../loop/model/loop-run';
import { ResearchStep, stepSubject } from '../../loop/model/research-step';

const SHOWN_STEPS = 3;

/** Research as it goes: how long it's been at it and its latest steps, watched while shown. */
@Component({
  selector: 'app-run-progress',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex min-w-0 flex-col gap-2' },
  styles: `
    .status {
      display: inline-flex;
      align-items: center;
      gap: 7px;
      font-family: var(--font-mono);
      font-size: 12px;
      color: var(--color-dim);
    }
    .status .spinner {
      color: var(--color-accent);
    }
    .steps {
      margin: 0;
      padding: 0;
      list-style: none;
      display: flex;
      flex-direction: column;
      gap: 5px;
      font-family: var(--font-mono);
      font-size: 11.5px;
      color: var(--color-muted);
    }
    .step {
      min-width: 0;
      display: flex;
      align-items: baseline;
      gap: 10px;
    }
    .verb {
      flex: none;
      width: 64px;
      color: var(--color-faint);
    }
  `,
  template: `
    <span class="status">
      <span class="spinner h-2.5 w-2.5" aria-hidden="true"></span>
      {{ run().state === 'queued' ? 'waiting to start' : 'researching' }}
      @if (clock(); as time) {
        · {{ time }}
      }
    </span>
    @if (shown().length) {
      <ol class="steps">
        @for (step of shown(); track $index) {
          <li class="step">
            <span class="verb">{{ step.action }}</span>
            <span class="truncate">{{ stepSubject(step) }}</span>
          </li>
        }
      </ol>
    }
  `,
})
export class RunProgress implements OnInit {
  private readonly runs = inject(LoopRuns);

  readonly run = input.required<LoopRun>();

  protected readonly stepSubject = stepSubject;
  private readonly steps = signal<ResearchStep[]>([]);
  private readonly now = signal(Date.now());
  private readonly watching = new AbortController();

  protected readonly shown = computed(() => this.steps().slice(-SHOWN_STEPS));
  protected readonly clock = computed(() => {
    const started = this.run().started;
    if (!started) {
      return '';
    }
    const s = Math.max(0, Math.floor((this.now() - started.getTime()) / 1000));
    return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  });

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(timer);
      this.watching.abort();
    });
  }

  ngOnInit(): void {
    void this.watch();
  }

  private async watch(): Promise<void> {
    try {
      for await (const update of this.runs.watch(this.run(), this.watching.signal)) {
        this.steps.update((steps) => [...(update.restarted ? [] : steps), ...update.steps]);
      }
    } catch {
      // The tray learns how it ended from the runs list.
    }
  }
}
