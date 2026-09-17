import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { PHASE_LABELS, phaseStateClass, phaseStateLabel } from '../segmentation/phases';
import { PhaseState, type Phase } from '../../lib/gen/segmentation_pb';

/**
 * The twelve-phase progress ladder.
 *
 * Skipped phases are shown rather than hidden, because "labelling was skipped" is the most
 * interesting thing that can happen to a clean document — it is why the run finished in seconds
 * and cost nothing (plan §9.4), and a hidden row would leave that unexplained.
 */
@Component({
  selector: 'app-phase-ladder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol class="space-y-1">
      @for (row of rows(); track row.index) {
        <li class="flex items-start gap-3 rounded-[var(--radius-md)] px-2 py-1.5" [class]="row.cssClass">
          <span class="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center text-xs" aria-hidden="true">
            @switch (row.state) {
              @case (PhaseState.DONE) { <span>✓</span> }
              @case (PhaseState.RUNNING) { <span class="animate-pulse">●</span> }
              @case (PhaseState.FAILED) { <span>✕</span> }
              @case (PhaseState.SKIPPED) { <span>–</span> }
              @default { <span class="opacity-40">○</span> }
            }
          </span>

          <span class="min-w-0 flex-1">
            <span class="flex flex-wrap items-baseline gap-x-2">
              <span class="font-medium">{{ row.name }}</span>
              <span class="text-xs text-[var(--color-neutral-500)]">{{ row.stateLabel }}</span>
              @if (row.duration) {
                <span class="text-xs text-[var(--color-neutral-600)]">{{ row.duration }}</span>
              }
            </span>
            <span class="block text-xs text-[var(--color-neutral-500)]">{{ row.blurb }}</span>
            @if (row.error) {
              <span class="mt-1 block font-mono text-xs text-[#f0a3a3]">{{ row.error }}</span>
            }
          </span>
        </li>
      }
    </ol>
  `,
  styles: `
    .phase-done { color: var(--color-text); }
    .phase-running { color: var(--color-accent-300); background-color: var(--color-surface); }
    .phase-failed { color: #f0a3a3; background-color: color-mix(in srgb, #f0a3a3 8%, transparent); }
    .phase-skipped { color: var(--color-neutral-500); }
    .phase-pending { color: var(--color-neutral-600); }
  `,
})
export class PhaseLadder {
  readonly phases = input.required<readonly Phase[]>();

  protected readonly PhaseState = PhaseState;

  protected readonly rows = computed(() => {
    const byIndex = new Map(this.phases().map((p) => [p.index, p]));

    return PHASE_LABELS.map((label) => {
      const phase = byIndex.get(label.index);
      const state = phase?.state ?? PhaseState.PENDING;

      return {
        index: label.index,
        name: label.name,
        blurb: label.blurb,
        state,
        stateLabel: phaseStateLabel(state),
        cssClass: phaseStateClass(state),
        duration: formatDuration(phase),
        error: phase?.error || null,
      };
    });
  });
}

/** Elapsed time for a finished phase, or nothing. A running phase's clock is noise. */
function formatDuration(phase: Phase | undefined): string | null {
  if (!phase || phase.startedUnixSeconds === 0n || phase.finishedUnixSeconds === 0n) {
    return null;
  }

  const seconds = Number(phase.finishedUnixSeconds - phase.startedUnixSeconds);
  if (seconds < 1) {
    return '<1s';
  }

  return seconds < 60 ? `${seconds}s` : `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
}
