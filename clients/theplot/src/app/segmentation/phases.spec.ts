import { isTerminal, phaseLabel, phaseStateLabel, PHASE_LABELS } from './phases';
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
});
