import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { DatePipe, PercentPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import type {
  ArtifactRef,
  StageAttempt as StageAttemptMessage,
} from '../../lib/gen/Admin/Theplot/theplot_engine_pb';
import {
  duration,
  NEUTRAL_BADGE,
  shortHash,
  TIER_CLASS,
  TIER_LABEL,
  toDate,
  usd,
  VERDICT_CLASS,
  VERDICT_LABEL,
} from './format';

/** One attempt at a stage: who ran it, what it read and wrote, and what the verifiers said. */
@Component({
  selector: 'app-stage-attempt',
  imports: [DatePipe, PercentPipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let stage = attempt();
    <article class="rounded-2xl border border-gray-200 bg-white shadow-sm">
      <header class="flex flex-wrap items-center gap-2 border-b border-gray-100 px-5 py-3">
        <span
          class="flex h-6 w-6 items-center justify-center rounded-full bg-gray-900 text-xs font-semibold text-white"
        >
          {{ index() + 1 }}
        </span>
        <h3 class="text-base font-semibold text-gray-900">{{ stage.stageId }}</h3>
        <span [class]="tierClass[stage.tier]">{{ tierLabel[stage.tier] }}</span>
        @if (stage.shadow) {
          <span
            class="{{ neutralBadge }}"
            title="Ran alongside the primary; its output was never used"
            >shadow</span
          >
        }
        <span [class]="resultClass(stage)">{{ resultLabel(stage) }}</span>
        <span class="ml-auto text-xs text-gray-500 whitespace-nowrap">
          {{ usd(stage.spend?.usdMicros) }} · {{ duration(stage.spend?.durationMs) }} ·
          {{ toDate(stage.recordedUnixMs) | date: 'HH:mm:ss' }}
        </span>
      </header>

      <div class="grid gap-4 px-5 py-4 text-sm sm:grid-cols-2">
        <div>
          <dt class="text-gray-500">Executor</dt>
          <dd class="mt-0.5">
            <a
              [routerLink]="['/executors', stage.executorId, stage.executorVersion]"
              class="font-mono text-indigo-600 hover:underline"
              >{{ stage.executorId }}&#64;{{ stage.executorVersion }}</a
            >
          </dd>
        </div>
        <div>
          <dt class="text-gray-500">Contract</dt>
          <dd class="mt-0.5 font-mono text-xs text-gray-900">
            {{ stage.inputContract?.schemaId }}&#64;{{ stage.inputContract?.version }} →
            {{ stage.outputContract?.schemaId }}&#64;{{ stage.outputContract?.version }}
          </dd>
          @if (stage.dependsOn.length) {
            <dd class="mt-0.5 text-xs text-gray-500">after {{ stage.dependsOn.join(', ') }}</dd>
          }
        </div>
        <div>
          <dt class="text-gray-500">Read</dt>
          <dd class="mt-1 flex flex-wrap gap-1.5">
            @for (ref of stage.inputs; track ref.hash + ref.stageId) {
              <button
                type="button"
                class="rounded-lg border border-gray-300 bg-gray-50 px-2 py-1 font-mono text-xs text-gray-800 hover:border-indigo-400 hover:bg-indigo-50"
                (click)="open.emit(ref)"
              >
                {{ ref.stageId }} · {{ shortHash(ref.hash) }}
              </button>
            } @empty {
              <span class="text-gray-400">nothing</span>
            }
          </dd>
        </div>
        <div>
          <dt class="text-gray-500">Wrote</dt>
          <dd class="mt-1">
            @if (stage.output; as ref) {
              <button
                type="button"
                class="rounded-lg border border-indigo-300 bg-indigo-50 px-2 py-1 font-mono text-xs text-indigo-900 hover:bg-indigo-100"
                (click)="open.emit(ref)"
              >
                {{ ref.stageId }} · {{ shortHash(ref.hash) }}
              </button>
            } @else {
              <span class="text-gray-400">no output</span>
            }
          </dd>
        </div>
      </div>

      @if (stage.verdicts.length) {
        <div class="border-t border-gray-100 px-5 py-4 text-sm">
          <dt class="text-gray-500">Verifiers</dt>
          <ul class="mt-2 space-y-2">
            @for (verdict of stage.verdicts; track verdict.verifierId) {
              <li>
                <div class="flex flex-wrap items-center gap-2">
                  <span [class]="verdictClass[verdict.verdict]">{{
                    verdictLabel[verdict.verdict]
                  }}</span>
                  <span class="font-mono text-xs text-gray-900">{{ verdict.verifierId }}</span>
                  @if (verdict.reason) {
                    <span class="text-gray-600">{{ verdict.reason }}</span>
                  }
                </div>
                @if (verdict.findings.length) {
                  <ul class="mt-1 ml-4 list-disc space-y-0.5 text-xs text-gray-600">
                    @for (finding of verdict.findings; track $index) {
                      <li>
                        <span class="font-mono text-gray-500">{{ finding.path }}</span>
                        {{ finding.message }}
                      </li>
                    }
                  </ul>
                }
              </li>
            }
          </ul>
        </div>
      }

      @if (stage.decisions.length) {
        <div class="border-t border-gray-100 px-5 py-4 text-sm">
          <dt class="text-gray-500">Decisions</dt>
          <ul class="mt-2 space-y-1.5">
            @for (decision of stage.decisions; track $index) {
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
        </div>
      }

      @if (stage.traceId) {
        <p class="border-t border-gray-100 px-5 py-2 font-mono text-xs text-gray-400">
          trace {{ stage.traceId }}
        </p>
      }
    </article>
  `,
})
export class StageAttempt {
  readonly attempt = input.required<StageAttemptMessage>();
  readonly index = input.required<number>();
  readonly open = output<ArtifactRef>();

  protected readonly tierClass = TIER_CLASS;
  protected readonly tierLabel = TIER_LABEL;
  protected readonly verdictClass = VERDICT_CLASS;
  protected readonly verdictLabel = VERDICT_LABEL;
  protected readonly neutralBadge = NEUTRAL_BADGE;
  protected readonly duration = duration;
  protected readonly shortHash = shortHash;
  protected readonly toDate = toDate;
  protected readonly usd = usd;

  protected resultLabel(stage: StageAttemptMessage): string {
    return !stage.passed ? 'Failed' : stage.degraded ? 'Degraded' : 'Passed';
  }

  protected resultClass(stage: StageAttemptMessage): string {
    const badge = 'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1';
    return !stage.passed
      ? `${badge} bg-red-50 text-red-800 ring-red-200`
      : stage.degraded
        ? `${badge} bg-amber-50 text-amber-800 ring-amber-200`
        : `${badge} bg-emerald-50 text-emerald-800 ring-emerald-200`;
  }
}
