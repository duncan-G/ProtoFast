import { create } from '@bufbuild/protobuf';
import {
  HistoryStoryRunSchema,
  HistoryStoryRunStatus,
} from '../../lib/gen/Admin/Theplot/theplot_history_pb';
import { parseBrief } from './brief';
import { isRunActive, runElapsed, runLabel } from './format';

describe('history format', () => {
  it('knows which statuses are still going', () => {
    expect(isRunActive(HistoryStoryRunStatus.WRITING)).toBe(true);
    expect(isRunActive(HistoryStoryRunStatus.CANCELLED)).toBe(false);
  });

  it('measures a run from its start, or its request while it waits', () => {
    const running = create(HistoryStoryRunSchema, { createdUnixMs: 1_000n, startedUnixMs: 5_000n });
    expect(runElapsed(running, 70_000)).toBe('1m 5s');
    const queued = create(HistoryStoryRunSchema, { createdUnixMs: 1_000n });
    expect(runElapsed(queued, 3_500)).toBe('2.5 s');
  });

  it('labels a run by its title, then its request', () => {
    expect(runLabel(create(HistoryStoryRunSchema, { title: 'The Ride', request: 'Revere' }))).toBe(
      'The Ride',
    );
    expect(runLabel(create(HistoryStoryRunSchema, { request: 'Revere' }))).toBe('Revere');
    expect(runLabel(create(HistoryStoryRunSchema, {}))).toBe('A random moment');
  });

  it('reads a brief, and nothing from a missing or broken one', () => {
    expect(
      parseBrief('{"title":"T","moment":"m","when":"w","where":"x","summary":"s"}')?.title,
    ).toBe('T');
    expect(parseBrief('')).toBeNull();
    expect(parseBrief('{')).toBeNull();
  });
});
