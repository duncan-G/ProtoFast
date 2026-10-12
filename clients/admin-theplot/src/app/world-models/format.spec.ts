import { create } from '@bufbuild/protobuf';
import {
  WorldModelBuildHeaderSchema,
  WorldModelBuildStatus,
} from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { buildElapsed, isBuildActive, tokens } from './format';

describe('world model format', () => {
  it('measures a build from its start, or its request while it waits', () => {
    const running = create(WorldModelBuildHeaderSchema, {
      requestedUnixMs: 1_000n,
      startedUnixMs: 5_000n,
      status: WorldModelBuildStatus.RUNNING,
    });
    expect(buildElapsed(running, 70_000)).toBe('1m 5s');

    const queued = create(WorldModelBuildHeaderSchema, { requestedUnixMs: 1_000n });
    expect(buildElapsed(queued, 3_500)).toBe('2.5 s');
  });

  it('knows which statuses are still going', () => {
    expect(isBuildActive(WorldModelBuildStatus.RETRYING)).toBe(true);
    expect(isBuildActive(WorldModelBuildStatus.FINISHED)).toBe(false);
  });

  it('shortens token counts', () => {
    expect(tokens(950n)).toBe('950');
    expect(tokens(12_345n)).toBe('12.3k');
  });
});
