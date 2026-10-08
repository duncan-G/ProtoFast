import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { DatePipe, PercentPipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import type { ArtifactRef, GetRunReply } from '../../../lib/gen/Admin/Theplot/theplot_engine_pb';
import { ArtifactPanel } from '../../engine/artifact-panel';
import {
  artifactParam,
  elapsed,
  NEUTRAL_BADGE,
  OUTCOME_CLASS,
  OUTCOME_LABEL,
  outcome,
  parseArtifactParam,
  PHASE_LABEL,
  RUN_MODE_LABEL,
  toDate,
  usd,
} from '../../engine/format';
import { RunSummary } from '../../engine/run-summary';
import { RunTranscript } from '../../engine/run-transcript';
import { StageAttempt } from '../../engine/stage-attempt';
import { errorMessage, TheplotAdminApi } from '../../theplot-admin';

type Tab = 'summary' | 'stages' | 'transcript';

/**
 * One run: its outcome, the brief of what it did, every stage attempt with what it read and wrote,
 * and the agent's conversation.
 */
@Component({
  selector: 'app-run',
  imports: [
    ArtifactPanel,
    DatePipe,
    PercentPipe,
    RouterLink,
    RunSummary,
    RunTranscript,
    StageAttempt,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/runs" class="text-sm text-indigo-600 hover:underline">← Runs</a>

    @if (error()) {
      <p class="mt-6 rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    @if (reply(); as reply) {
      @let run = reply.run!;
      <header class="mt-4 flex flex-wrap items-center gap-3">
        <h1 class="font-mono text-xl font-bold text-gray-900">{{ run.runId }}</h1>
        <span [class]="outcomeClass[outcome(run)]">{{ outcomeLabel[outcome(run)] }}</span>
        <span class="{{ neutralBadge }}">{{ modeLabel[run.mode] }}</span>
        <a [routerLink]="['/families', run.family]" class="{{ neutralBadge }} hover:bg-gray-200">
          {{ run.family }}
          @if (run.generation > 0) {
            <span>&nbsp;gen {{ run.generation }}</span>
          }
        </a>
      </header>

      @if (run.failure) {
        <p class="mt-4 rounded-lg bg-red-50 border border-red-200 p-4 text-sm text-red-800">
          <span class="font-medium">Abandoned:</span> {{ run.failure }}
        </p>
      }

      <dl
        class="mt-6 grid gap-x-6 gap-y-4 rounded-2xl border border-gray-200 bg-white p-6 text-sm shadow-sm sm:grid-cols-3 lg:grid-cols-4"
      >
        <div>
          <dt class="text-gray-500">Opened</dt>
          <dd class="text-gray-900">{{ toDate(run.openedUnixMs) | date: 'medium' }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">{{ run.abandonedUnixMs ? 'Abandoned' : 'Closed' }}</dt>
          <dd class="text-gray-900">
            @if (toDate(run.abandonedUnixMs || run.closedUnixMs); as at) {
              {{ at | date: 'medium' }}
            } @else {
              still running
            }
          </dd>
        </div>
        <div>
          <dt class="text-gray-500">Took</dt>
          <dd class="text-gray-900">{{ elapsed(run) }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Cost</dt>
          <dd class="text-gray-900">{{ usd(run.costUsdMicros) }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Stage attempts</dt>
          <dd class="text-gray-900">{{ run.stageAttempts }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Source upload</dt>
          <dd class="font-mono text-xs text-gray-900">{{ run.sourceId || '—' }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Trace</dt>
          <dd class="font-mono text-xs text-gray-900">{{ run.traceId || '—' }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Import progress</dt>
          <dd class="text-gray-900">
            @if (reply.progress; as progress) {
              {{ phaseLabel[progress.phase] }}
              @if (progress.stageId) {
                <span class="text-gray-500">· {{ progress.stageId }}</span>
              }
              @if (progress.resultId) {
                <a
                  [routerLink]="['/stories', progress.resultId]"
                  class="ml-2 text-indigo-600 hover:underline"
                  >open the story →</a
                >
              }
              @if (progress.message) {
                <p class="mt-0.5 text-xs text-gray-600">{{ progress.message }}</p>
              }
            } @else {
              —
            }
          </dd>
        </div>
        @if (facets().length) {
          <div class="sm:col-span-3 lg:col-span-4">
            <dt class="text-gray-500">Classifier facets</dt>
            <dd class="mt-1 flex flex-wrap gap-1.5">
              @for (facet of facets(); track facet[0]) {
                <span class="{{ neutralBadge }} font-mono">{{ facet[0] }}={{ facet[1] }}</span>
              }
            </dd>
          </div>
        }
      </dl>

      <nav class="mt-8 flex gap-1 border-b border-gray-200 text-sm" aria-label="Run sections">
        @if (reply.messageCount) {
          <button type="button" [class]="tabClass('summary')" (click)="tab.set('summary')">
            Summary
          </button>
        }
        <button type="button" [class]="tabClass('stages')" (click)="tab.set('stages')">
          Stages <span class="ml-1 text-gray-400">{{ reply.stages.length }}</span>
        </button>
        <button type="button" [class]="tabClass('transcript')" (click)="tab.set('transcript')">
          Conversation <span class="ml-1 text-gray-400">{{ reply.messageCount }}</span>
        </button>
      </nav>

      @if (tab() === 'summary') {
        <section class="mt-6">
          <app-run-summary
            [runId]="run.runId"
            [ended]="!!(run.closedUnixMs || run.abandonedUnixMs)"
            (openMessage)="openMessage($event)"
          />
        </section>
      } @else if (tab() === 'stages') {
        @if (reply.decisions.length) {
          <section class="mt-6 rounded-2xl border border-gray-200 bg-white p-5 text-sm shadow-sm">
            <h2 class="font-semibold text-gray-900">Run decisions</h2>
            <p class="mt-1 text-xs text-gray-500">
              What the loop owner chose, for policy to learn from.
            </p>
            <ul class="mt-3 space-y-1.5">
              @for (decision of reply.decisions; track $index) {
                <li>
                  <span class="font-mono text-xs text-gray-500">{{ decision.key }}</span>
                  <span class="mx-1 text-gray-400">→</span>
                  <span class="font-medium text-gray-900">{{ decision.choice }}</span>
                  <span class="ml-1 text-xs text-gray-500"
                    >({{ decision.confidence | percent }})</span
                  >
                  @if (decision.rationale) {
                    <p class="text-gray-600">{{ decision.rationale }}</p>
                  }
                </li>
              }
            </ul>
          </section>
        }

        <section class="mt-6 space-y-4">
          @for (stage of reply.stages; track $index) {
            <app-stage-attempt [attempt]="stage" [index]="$index" (open)="show($event)" />
          } @empty {
            <p
              class="rounded-2xl border border-dashed border-gray-300 p-8 text-center text-sm text-gray-500"
            >
              No stage attempt has been recorded yet.
            </p>
          }
        </section>
      } @else {
        <section class="mt-6">
          <app-run-transcript [runId]="run.runId" [focus]="focus()" />
        </section>
      }
    } @else if (!error()) {
      <p class="mt-6 text-sm text-gray-500">Loading…</p>
    }

    @if (selected(); as ref) {
      <app-artifact-panel [ref]="ref" (closed)="show(null)" />
    }
  `,
})
export class Run {
  private readonly api = inject(TheplotAdminApi);
  private readonly router = inject(Router);

  readonly id = input.required<string>();
  /** `?artifact=runId/stageId/hash`, so an artifact can be linked to directly. */
  readonly artifact = input<string>('');

  protected readonly outcomeClass = OUTCOME_CLASS;
  protected readonly outcomeLabel = OUTCOME_LABEL;
  protected readonly modeLabel = RUN_MODE_LABEL;
  protected readonly phaseLabel = PHASE_LABEL;
  protected readonly neutralBadge = NEUTRAL_BADGE;
  protected readonly outcome = outcome;
  protected readonly elapsed = elapsed;
  protected readonly toDate = toDate;
  protected readonly usd = usd;

  protected readonly reply = signal<GetRunReply | null>(null);
  protected readonly error = signal('');
  protected readonly tab = signal<Tab>('stages');
  protected readonly focus = signal<number | null>(null);
  protected readonly facets = computed(() => Object.entries(this.reply()?.run?.facets ?? {}));
  protected readonly selected = computed(() =>
    this.artifact() ? parseArtifactParam(this.artifact()) : null,
  );

  constructor() {
    afterNextRender(() => void this.load());
  }

  protected show(ref: ArtifactRef | null): void {
    void this.router.navigate([], {
      queryParams: { artifact: ref ? artifactParam(ref) : null },
      queryParamsHandling: 'merge',
    });
  }

  protected openMessage(sequence: number): void {
    this.focus.set(sequence);
    this.tab.set('transcript');
  }

  protected tabClass(tab: Tab): string {
    const base = '-mb-px border-b-2 px-3 py-2 font-medium';
    return this.tab() === tab
      ? `${base} border-indigo-600 text-indigo-700`
      : `${base} border-transparent text-gray-500 hover:text-gray-700`;
  }

  private async load(): Promise<void> {
    try {
      const reply = await this.api.runs.getRun({ runId: this.id() });
      this.reply.set(reply);
      if (reply.messageCount) {
        this.tab.set('summary');
      }
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }
}
