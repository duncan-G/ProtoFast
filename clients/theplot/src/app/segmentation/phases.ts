import { PhaseState } from '../../lib/gen/segmentation_pb';

/**
 * The eighteen pipeline phases, named for a reader rather than for the code.
 *
 * The indices are the scene plan's (§8.1) and are also the `RerunFrom` argument, so they are
 * pinned: a phase's number appears in artifact keys and in the proto, and renumbering here would
 * silently re-run the wrong phase.
 *
 * The five scene phases were INSERTED rather than appended, which renumbered everything from
 * Presentation onwards. That keeps the artifact prefixes in reading order, which is the whole
 * reason the numbers are the prefixes.
 */
export const PHASE_LABELS: readonly { index: number; name: string; blurb: string }[] = [
  { index: 0, name: 'Ingest', blurb: 'Converting the document and reading its layout' },
  { index: 1, name: 'Clean', blurb: 'Removing running heads, page numbers and hyphenation' },
  { index: 2, name: 'Triage', blurb: 'Deciding which regions need a model' },
  { index: 3, name: 'Label', blurb: 'Labelling every line' },
  { index: 4, name: 'Assemble', blurb: 'Building the paragraphs' },
  { index: 5, name: 'Presentation', blurb: 'Separating the work from its front and back matter' },
  { index: 6, name: 'Structure', blurb: 'Inferring the section tree' },
  { index: 7, name: 'Validate', blurb: 'Checking the result against the source' },
  { index: 8, name: 'Items', blurb: 'Typing speech, action, description and exhibits' },
  { index: 9, name: 'Referents', blurb: 'Resolving who and where into a cast list' },
  { index: 10, name: 'Scenes', blurb: 'Cutting the scenes' },
  { index: 11, name: 'Links', blurb: 'Finding flashbacks and framing stories' },
  { index: 12, name: 'Review', blurb: 'A second opinion on the structure' },
  { index: 13, name: 'Approval', blurb: 'Waiting for a person' },
  { index: 14, name: 'Freeze', blurb: 'Making the structure permanent' },
  { index: 15, name: 'Augment', blurb: 'Producing per-paragraph and per-item output' },
  { index: 16, name: 'Check', blurb: 'Reviewing a sample of the augmentations' },
  { index: 17, name: 'Publish', blurb: 'Writing the result' },
];

/** The last phase. A run that reaches it has nothing further to do. */
export const PUBLISH_PHASE = PHASE_LABELS[PHASE_LABELS.length - 1].index;

export function phaseLabel(index: number): string {
  return PHASE_LABELS[index]?.name ?? `Phase ${index}`;
}

export function phaseStateClass(state: PhaseState): string {
  switch (state) {
    case PhaseState.DONE:
      return 'phase-done';
    case PhaseState.RUNNING:
      return 'phase-running';
    case PhaseState.FAILED:
      return 'phase-failed';
    case PhaseState.SKIPPED:
      return 'phase-skipped';
    default:
      return 'phase-pending';
  }
}

export function phaseStateLabel(state: PhaseState): string {
  switch (state) {
    case PhaseState.DONE:
      return 'done';
    case PhaseState.RUNNING:
      return 'running';
    case PhaseState.FAILED:
      return 'failed';
    case PhaseState.SKIPPED:
      return 'skipped';
    default:
      return 'waiting';
  }
}

/** A run is finished when nothing further will happen without someone asking for it. */
export function isTerminal(run: { cancelled: boolean; error: string; phases: { index: number; state: PhaseState }[] }): boolean {
  if (run.cancelled || run.error) {
    return true;
  }

  return run.phases.some((p) => p.index === PUBLISH_PHASE && p.state === PhaseState.DONE);
}

/**
 * Where a re-run should start by default: the phase that failed, or — for a cancelled run, which
 * has no failed phase — the first one that never finished.
 *
 * Re-running a phase that is already done is the expensive answer (every phase at or after the
 * chosen one bypasses its idempotency gate and calls a provider again), so it is never the one
 * offered. A run with nothing left to point at falls back to phase 0, which is the honest reading
 * of "this never got anywhere".
 */
export function suggestedRerunPhase(
  phases: readonly { index: number; state: PhaseState }[],
): number {
  const failed = phases.find((p) => p.state === PhaseState.FAILED);
  if (failed) {
    return failed.index;
  }

  const unfinished = phases
    .filter((p) => p.state !== PhaseState.DONE && p.state !== PhaseState.SKIPPED)
    .map((p) => p.index)
    .sort((a, b) => a - b);

  return unfinished[0] ?? 0;
}
