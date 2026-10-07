import { create } from '@bufbuild/protobuf';
import { RunHeaderSchema, RunStatus } from '../../lib/gen/Admin/Theplot/theplot_engine_pb';
import {
  duration,
  elapsed,
  familyKey,
  outcome,
  parseArtifactParam,
  prettyJson,
  usd,
} from './format';

describe('engine formatting', () => {
  it('shows money to the cent, or finer when a run cost less than one', () => {
    expect(usd(0n)).toBe('$0');
    expect(usd(1_250_000n)).toBe('$1.25');
    expect(usd(2_500n)).toBe('$0.0025');
  });

  it('shows a duration in the unit that fits it', () => {
    expect(duration(420)).toBe('420 ms');
    expect(duration(4_200n)).toBe('4.2 s');
    expect(duration(125_000)).toBe('2m 5s');
    expect(duration(3_900_000)).toBe('1h 5m');
  });

  it('derives a run’s outcome from its status and whether its stages passed', () => {
    const run = (status: RunStatus, passed: boolean) =>
      create(RunHeaderSchema, { status, passed, openedUnixMs: 1_000n, closedUnixMs: 61_000n });
    expect(outcome(run(RunStatus.OPEN, false))).toBe('open');
    expect(outcome(run(RunStatus.CLOSED, true))).toBe('passed');
    expect(outcome(run(RunStatus.CLOSED, false))).toBe('failed');
    expect(outcome(run(RunStatus.ABANDONED, true))).toBe('abandoned');
    expect(elapsed(run(RunStatus.CLOSED, true))).toBe('1m 0s');
    expect(elapsed(create(RunHeaderSchema, { openedUnixMs: 1_000n }), 3_500)).toBe('2.5 s');
  });

  it('round-trips an artifact through its deep link, keeping slashes inside the stage id', () => {
    const ref = parseArtifactParam('upload-1/units/scene-3/abc123');
    expect(ref).toMatchObject({ runId: 'upload-1', stageId: 'units/scene-3', hash: 'abc123' });
    expect(parseArtifactParam('run-1/nohash')).toBeNull();
    expect(parseArtifactParam('run-1/stage/')).toBeNull();
    expect(parseArtifactParam('run-1//abc')).toBeNull();
  });

  it('pretty-prints JSON and leaves other text alone', () => {
    expect(prettyJson('{"a":1}')).toBe('{\n  "a": 1\n}');
    expect(prettyJson('INT. HOUSE - DAY')).toBeNull();
    expect(prettyJson('{not json')).toBeNull();
  });

  it('keys generation zero by the bare family name', () => {
    expect(familyKey('prose', 0)).toBe('prose');
    expect(familyKey('prose', 2)).toBe('prose#2');
  });
});
