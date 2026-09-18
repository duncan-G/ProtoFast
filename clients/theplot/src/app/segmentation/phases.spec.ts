import {
  isTerminal,
  phaseLabel,
  phaseStateLabel,
  suggestedRerunPhase,
  PHASE_LABELS,
} from './phases';
import { PhaseState } from '../../lib/gen/segmentation_pb';

describe('phases', () => {
  it('numbers the phases exactly as the pipeline does', () => {
    // The indices are the artifact prefixes and the RerunFrom argument, so a renumber here would
    // silently re-run the wrong phase (plan §9.1).
    expect(PHASE_LABELS.map((p) => p.index)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
    expect(phaseLabel(9)).toBe('Freeze');
    expect(phaseLabel(99)).toBe('Phase 99');
  });

  it('names every phase state', () => {
    expect(phaseStateLabel(PhaseState.SKIPPED)).toBe('skipped');
    expect(phaseStateLabel(PhaseState.PHASE_STATE_UNSPECIFIED)).toBe('waiting');
  });

  it('treats a published run as finished', () => {
    expect(
      isTerminal({
        cancelled: false,
        error: '',
        phases: [{ index: 12, state: PhaseState.DONE }],
      }),
    ).toBe(true);
  });

  it('treats a cancelled or failed run as finished even mid-pipeline', () => {
    const phases = [{ index: 3, state: PhaseState.RUNNING }];
    expect(isTerminal({ cancelled: true, error: '', phases })).toBe(true);
    expect(isTerminal({ cancelled: false, error: 'boom', phases })).toBe(true);
  });

  it('does not treat a run still in flight as finished', () => {
    expect(
      isTerminal({
        cancelled: false,
        error: '',
        phases: [{ index: 5, state: PhaseState.RUNNING }],
      }),
    ).toBe(false);
  });

  it('suggests the failed phase for a run that stopped', () => {
    expect(
      suggestedRerunPhase([
        { index: 0, state: PhaseState.DONE },
        { index: 1, state: PhaseState.DONE },
        { index: 2, state: PhaseState.SKIPPED },
        { index: 3, state: PhaseState.FAILED },
      ]),
    ).toBe(3);
  });

  it('suggests the first unfinished phase for a cancelled run, which has no failed phase', () => {
    expect(
      suggestedRerunPhase([
        { index: 0, state: PhaseState.DONE },
        { index: 1, state: PhaseState.SKIPPED },
        { index: 2, state: PhaseState.RUNNING },
        { index: 3, state: PhaseState.PENDING },
      ]),
    ).toBe(2);
  });

  it('never suggests re-running work that is already done', () => {
    // Every phase at or after the chosen one bypasses its idempotency gate, so suggesting a done
    // phase would offer to re-buy output that already exists.
    const done = [
      { index: 0, state: PhaseState.DONE },
      { index: 1, state: PhaseState.DONE },
    ];

    expect(suggestedRerunPhase(done)).toBe(0);
    expect(suggestedRerunPhase([])).toBe(0);
  });
});
