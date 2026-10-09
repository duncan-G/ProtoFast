import { inject, Injectable } from '@angular/core';
import { createClient } from '@connectrpc/connect';
import {
  type Cue as CueMessage,
  Direction,
  type DirectRequest,
  type Frame as FrameMessage,
  type SceneAppearance as SceneAppearanceMessage,
  type SceneDirection as SceneDirectionMessage,
  type SceneLocationState as SceneLocationStateMessage,
  type ScenePropState as ScenePropStateMessage,
} from '../../lib/gen/direction_pb';
import { GRPC_TRANSPORT } from '../grpc-transport';
import { fromScene, toCharacter, toLocation, toProp } from '../stories/connect-story-api';
import { Character } from '../stories/model/character';
import { Location } from '../stories/model/location';
import { Prop } from '../stories/model/prop';
import { Scene } from '../stories/model/scene';
import type { DirectionApi } from './direction-api';
import { Cue } from './model/cue';
import { DescribeTarget } from './model/describe-target';
import { DirectTarget } from './model/direct-target';
import { Frame } from './model/frame';
import { SceneAppearance } from './model/scene-appearance';
import { SceneDirection } from './model/scene-direction';
import { SceneLocationState } from './model/scene-location-state';
import { ScenePropState } from './model/scene-prop-state';
import { SpeechVoice } from './model/speech-voice';

/** The direction endpoints on the api service, over gRPC-Web through the edge, like `ConnectStoryApi`. */
@Injectable({ providedIn: 'root' })
export class ConnectDirectionApi implements DirectionApi {
  private readonly client = createClient(Direction, inject(GRPC_TRANSPORT));

  async getSceneDirection(sceneId: string): Promise<SceneDirection> {
    const reply = await this.client.getSceneDirection({ sceneId });
    return toDirection(required(reply.direction));
  }

  async saveSceneDirection(direction: SceneDirection): Promise<SceneDirection> {
    const reply = await this.client.saveSceneDirection({ direction: fromDirection(direction) });
    return toDirection(required(reply.direction));
  }

  async saveDirectedScene(scene: Scene, direction: SceneDirection): Promise<SceneDirection> {
    const reply = await this.client.saveDirectedScene({
      scene: fromScene(scene),
      direction: fromDirection(direction),
    });
    return toDirection(required(reply.direction));
  }

  async direct(
    sceneId: string,
    target: DirectTarget,
    replaceEdited = false,
  ): Promise<SceneDirection> {
    const reply = await this.client.direct({ sceneId, replaceEdited, target: fromTarget(target) });
    return toDirection(required(reply.direction));
  }

  async generateAudio(
    sceneId: string,
    elementId: string | null = null,
    regenerate = false,
  ): Promise<SceneDirection> {
    const reply = await this.client.generateAudio({
      sceneId,
      elementId: elementId ?? undefined,
      regenerate,
    });
    return toDirection(required(reply.direction));
  }

  async listVoices(): Promise<{ provider: string; voices: SpeechVoice[] }> {
    const reply = await this.client.listVoices({});
    return {
      provider: reply.provider,
      voices: reply.voices.map((v) => ({ id: v.id, name: v.name })),
    };
  }

  async describe(target: DescribeTarget): Promise<Character | Location | Prop> {
    const subject =
      target.kind === 'character'
        ? ({ case: 'characterId', value: target.id } as const)
        : target.kind === 'location'
          ? ({ case: 'locationId', value: target.id } as const)
          : ({ case: 'propId', value: target.id } as const);
    const reply = await this.client.describe({ subject });
    switch (reply.subject.case) {
      case 'character':
        return toCharacter(reply.subject.value);
      case 'location':
        return toLocation(reply.subject.value);
      case 'prop':
        return toProp(reply.subject.value);
      default:
        throw new Error('The API returned an empty reply.');
    }
  }
}

function required<T>(message: T | undefined): T {
  if (message === undefined) {
    throw new Error('The API returned an empty reply.');
  }
  return message;
}

function fromTarget(target: DirectTarget): DirectRequest['target'] {
  switch (target.kind) {
    case 'scene':
      return { case: 'scene', value: true };
    case 'storyDay':
      return { case: 'storyDay', value: true };
    case 'continuity':
      return { case: 'continuity', value: true };
    case 'appearance':
      return { case: 'appearanceCharacterId', value: target.characterId };
    case 'propState':
      return { case: 'propStatePropId', value: target.propId };
    case 'cues':
      return { case: 'cues', value: true };
    case 'cue':
      return { case: 'cueElementId', value: target.elementId };
    case 'frames':
      return { case: 'frames', value: true };
    case 'frame':
      return { case: 'frameId', value: target.frameId };
  }
}

export function toDirection(message: SceneDirectionMessage): SceneDirection {
  return {
    sceneId: message.sceneId,
    storyDay: message.storyDay ?? null,
    shot: message.shot ?? null,
    angle: message.angle ?? null,
    movement: message.movement ?? null,
    frames: message.frames.map(toFrame),
    cues: message.cues.map(toCue),
    appearances: message.appearances.map(toAppearance),
    propStates: message.propStates.map(toPropState),
    locationStates: message.locationStates.map(toLocationState),
  };
}

function toFrame(message: FrameMessage): Frame {
  return {
    id: message.id,
    position: message.position,
    firstElementId: message.firstElementId,
    lastElementId: message.lastElementId,
    locationId: message.locationId ?? null,
    timeOfDay: message.timeOfDay ?? null,
    beat: message.beat,
    shot: message.shot ?? null,
    angle: message.angle ?? null,
    movement: message.movement ?? null,
    locationChange: message.locationChange,
    holdMs: message.holdMs ?? null,
    placements: message.placements.map((p) => ({
      characterId: p.characterId,
      screen: p.screen,
      pose: p.pose,
      expression: p.expression,
      onScreen: p.onScreen ?? null,
      wardrobeId: p.wardrobeId ?? null,
      wardrobeChange: p.wardrobeChange,
      appearanceChange: p.appearanceChange,
    })),
    props: message.props.map((p) => ({ propId: p.propId, inView: p.inView })),
    edited: message.edited,
  };
}

function toCue(message: CueMessage): Cue {
  return {
    id: message.id,
    elementId: message.elementId,
    performance: message.performance,
    gapBeforeMs: message.gapBeforeMs,
    interrupted: message.interrupted,
    narratorId: message.narratorId ?? null,
    edited: message.edited,
    audio: message.audio
      ? {
          assetId: message.audio.assetId,
          url: message.audio.url,
          contentType: message.audio.contentType,
          stale: message.audio.stale,
          voice: message.audio.voice,
        }
      : null,
  };
}

function toAppearance(message: SceneAppearanceMessage): SceneAppearance {
  return {
    id: message.id,
    characterId: message.characterId,
    onScreen: message.onScreen ?? true,
    wardrobeId: message.wardrobeId ?? null,
    wardrobeChange: message.wardrobeChange,
    appearanceChange: message.appearanceChange,
    changeReason: message.changeReason ?? null,
    changeElementId: message.changeElementId ?? null,
    edited: message.edited,
  };
}

function toPropState(message: ScenePropStateMessage): ScenePropState {
  return {
    id: message.id,
    propId: message.propId,
    inView: message.inView ?? true,
    state: [...message.state],
    changeReason: message.changeReason ?? null,
    changeElementId: message.changeElementId ?? null,
    edited: message.edited,
  };
}

function toLocationState(message: SceneLocationStateMessage): SceneLocationState {
  return {
    id: message.id,
    locationId: message.locationId,
    change: message.change,
    edited: message.edited,
  };
}

export function fromDirection(direction: SceneDirection) {
  return {
    sceneId: direction.sceneId,
    storyDay: direction.storyDay ?? undefined,
    shot: direction.shot ?? undefined,
    angle: direction.angle ?? undefined,
    movement: direction.movement ?? undefined,
    frames: direction.frames.map((p, position) => ({
      id: p.id,
      position,
      firstElementId: p.firstElementId,
      lastElementId: p.lastElementId,
      locationId: p.locationId ?? undefined,
      timeOfDay: p.timeOfDay ?? undefined,
      beat: p.beat,
      shot: p.shot ?? undefined,
      angle: p.angle ?? undefined,
      movement: p.movement ?? undefined,
      locationChange: p.locationChange,
      holdMs: p.holdMs ?? undefined,
      placements: p.placements.map((pl) => ({
        ...pl,
        onScreen: pl.onScreen ?? undefined,
        wardrobeId: pl.wardrobeId ?? undefined,
      })),
      props: p.props.map((pp) => ({ ...pp })),
      edited: p.edited,
    })),
    cues: direction.cues.map(({ audio: _audio, ...cue }) => ({
      ...cue,
      narratorId: cue.narratorId ?? undefined,
    })),
    appearances: direction.appearances.map((a) => ({
      id: a.id,
      characterId: a.characterId,
      onScreen: a.onScreen,
      wardrobeId: a.wardrobeId ?? undefined,
      wardrobeChange: a.wardrobeChange,
      appearanceChange: a.appearanceChange,
      changeReason: a.changeReason ?? undefined,
      changeElementId: a.changeElementId ?? undefined,
      edited: a.edited,
    })),
    propStates: direction.propStates.map((p) => ({
      id: p.id,
      propId: p.propId,
      inView: p.inView,
      state: [...p.state],
      changeReason: p.changeReason ?? undefined,
      changeElementId: p.changeElementId ?? undefined,
      edited: p.edited,
    })),
    locationStates: direction.locationStates.map((l) => ({ ...l })),
  };
}
