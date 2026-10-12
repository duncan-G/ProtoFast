import { inject, Injectable } from '@angular/core';
import { createClient } from '@connectrpc/connect';
import { GRPC_TRANSPORT } from '../grpc-transport';
import {
  WorldModelBuildState as WorldModelBuildStateMessage,
  WorldModels,
  type WorldModelBuild as WorldModelBuildMessage,
} from '../../lib/gen/world_model_pb';
import { WorldModelBuild, WorldModelBuildState } from './world-model-build';

@Injectable({ providedIn: 'root' })
export class WorldModelApi {
  private readonly worldModels = createClient(WorldModels, inject(GRPC_TRANSPORT));

  /** Queues a build; FailedPrecondition while one is already queued or running. */
  async startBuild(storyId: string): Promise<WorldModelBuild> {
    const reply = await this.worldModels.startBuild({ storyId });
    return toBuild(required(reply.build));
  }

  /** The story's most recent build, or null when it has never had one. */
  async getLatestBuild(storyId: string): Promise<WorldModelBuild | null> {
    const reply = await this.worldModels.getLatestBuild({ storyId });
    return reply.build ? toBuild(reply.build) : null;
  }

  /** Stops a queued or running build; FailedPrecondition once it has ended. */
  async cancelBuild(buildId: string): Promise<WorldModelBuild> {
    const reply = await this.worldModels.cancelBuild({ buildId });
    return toBuild(required(reply.build));
  }
}

const BUILD_STATES: Record<WorldModelBuildStateMessage, WorldModelBuildState> = {
  [WorldModelBuildStateMessage.UNSPECIFIED]: 'queued',
  [WorldModelBuildStateMessage.QUEUED]: 'queued',
  [WorldModelBuildStateMessage.RUNNING]: 'running',
  [WorldModelBuildStateMessage.RETRYING]: 'retrying',
  [WorldModelBuildStateMessage.FINISHED]: 'finished',
  [WorldModelBuildStateMessage.FAILED]: 'failed',
  [WorldModelBuildStateMessage.CANCELLED]: 'cancelled',
};

function toBuild(message: WorldModelBuildMessage): WorldModelBuild {
  return {
    id: message.id,
    storyId: message.storyId,
    state: BUILD_STATES[message.state] ?? 'queued',
    scenesTotal: message.scenesTotal,
    scenesDone: message.scenesDone,
    message: message.message,
    costUsd: Number(message.costUsdMicros) / 1_000_000,
    requestedAt: new Date(Number(message.requestedUnixMs)),
    startedAt: toDate(message.startedUnixMs),
    finishedAt: toDate(message.finishedUnixMs),
    partCount: message.partCount,
    objectCount: message.objectCount,
    observationCount: message.observationCount,
  };
}

function toDate(unixMs: bigint): Date | null {
  return unixMs ? new Date(Number(unixMs)) : null;
}

function required(build: WorldModelBuildMessage | undefined): WorldModelBuildMessage {
  if (!build) {
    throw new Error('The API answered without the build.');
  }
  return build;
}
