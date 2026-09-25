import { inject, Injectable } from '@angular/core';
import { createClient } from '@connectrpc/connect';
import {
  AvatarShape as AvatarShapeMessage,
  type Character as CharacterMessage,
  type Container as ContainerMessage,
  LocationSetting as LocationSettingMessage,
  type Location as LocationMessage,
  type Prop as PropMessage,
  type SceneElement as SceneElementMessage,
  type SceneElementMention as SceneElementMentionMessage,
  SceneElementType as SceneElementTypeMessage,
  type Scene as SceneMessage,
  type SceneSummary as SceneSummaryMessage,
  Stories,
  type Story as StoryMessage,
  type StorySummary as StorySummaryMessage,
  type StoryVocabulary as StoryVocabularyMessage,
} from '../../lib/gen/story_pb';
import { GRPC_TRANSPORT } from '../grpc-transport';
import type { StoryApi } from './story-api';
import { AvatarShape } from './model/avatar-shape';
import { Character } from './model/character';
import { Container } from './model/container';
import { Location } from './model/location';
import { LocationSetting } from './model/location-setting';
import { Prop } from './model/prop';
import { Scene } from './model/scene';
import { SceneElement } from './model/scene-element';
import { SceneElementMention } from './model/scene-element-mention';
import { SceneElementType } from './model/scene-element-type';
import { SceneSummary } from './model/scene-summary';
import { Story } from './model/story';
import { StorySummary } from './model/story-summary';
import { StoryVocabulary } from './model/story-vocabulary';

const ELEMENT_TYPES: Record<SceneElementType, SceneElementTypeMessage> = {
  Heading: SceneElementTypeMessage.HEADING,
  Action: SceneElementTypeMessage.ACTION,
  Description: SceneElementTypeMessage.DESCRIPTION,
  Narration: SceneElementTypeMessage.NARRATION,
  Dialogue: SceneElementTypeMessage.DIALOGUE,
  Transition: SceneElementTypeMessage.TRANSITION,
};

const SETTINGS: Record<LocationSetting, LocationSettingMessage> = {
  Interior: LocationSettingMessage.INTERIOR,
  Exterior: LocationSettingMessage.EXTERIOR,
};

const SHAPES: Record<AvatarShape, AvatarShapeMessage> = {
  Circle: AvatarShapeMessage.CIRCLE,
  Square: AvatarShapeMessage.SQUARE,
  Squircle: AvatarShapeMessage.SQUIRCLE,
  Teardrop: AvatarShapeMessage.TEARDROP,
  Pill: AvatarShapeMessage.PILL,
  Diamond: AvatarShapeMessage.DIAMOND,
  Triangle: AvatarShapeMessage.TRIANGLE,
  Pentagon: AvatarShapeMessage.PENTAGON,
  Hexagon: AvatarShapeMessage.HEXAGON,
  Octagon: AvatarShapeMessage.OCTAGON,
  Star: AvatarShapeMessage.STAR,
  Shield: AvatarShapeMessage.SHIELD,
};

/**
 * The story endpoints on the api service, over gRPC-Web through the edge (`/api/…`), like
 * `DocumentApi`. Browser only: the editor loads after render.
 */
@Injectable({ providedIn: 'root' })
export class ConnectStoryApi implements StoryApi {
  private readonly client = createClient(Stories, inject(GRPC_TRANSPORT));

  async listStories(): Promise<StorySummary[]> {
    const reply = await this.client.listStories({});
    return reply.stories.map(toStorySummary);
  }

  async createStory(title: string): Promise<Story> {
    const reply = await this.client.createStory({ title });
    return toStory(required(reply.story));
  }

  async getStory(storyId: string): Promise<Story> {
    const reply = await this.client.getStory({ storyId });
    return toStory(required(reply.story));
  }

  async updateStory(storyId: string, title: string): Promise<void> {
    await this.client.updateStory({ storyId, title });
  }

  async deleteStory(storyId: string): Promise<void> {
    await this.client.deleteStory({ storyId });
  }

  async getScene(sceneId: string): Promise<Scene> {
    const reply = await this.client.getScene({ sceneId });
    return toScene(required(reply.scene));
  }

  async saveScene(scene: Scene): Promise<void> {
    await this.client.saveScene({ scene: fromScene(scene) });
  }

  async createScene(scene: Scene): Promise<Scene> {
    const reply = await this.client.createScene({ scene: fromScene(scene) });
    return toScene(required(reply.scene));
  }

  async createContainer(storyId: string, label: string): Promise<Container> {
    const reply = await this.client.createContainer({ storyId, label });
    return toContainer(required(reply.container));
  }

  async renameContainer(containerId: string, label: string): Promise<void> {
    await this.client.renameContainer({ containerId, label });
  }

  async createCharacter(
    storyId: string,
    character: Omit<Character, 'id' | 'storyId'>,
  ): Promise<Character> {
    const { name, kind, hue } = character;
    const reply = await this.client.createCharacter({ storyId, name, kind, hue });
    return toCharacter(required(reply.character));
  }

  async updateCharacter(character: Character): Promise<void> {
    const { id, name, kind, hue } = character;
    await this.client.updateCharacter({ characterId: id, name, kind, hue });
  }

  async deleteCharacter(characterId: string): Promise<void> {
    await this.client.deleteCharacter({ characterId });
  }

  async createLocation(
    storyId: string,
    location: Omit<Location, 'id' | 'storyId'>,
  ): Promise<Location> {
    const { name, setting, hue } = location;
    const reply = await this.client.createLocation({
      storyId,
      name,
      setting: SETTINGS[setting],
      hue,
    });
    return toLocation(required(reply.location));
  }

  async updateLocation(location: Location): Promise<void> {
    const { id, name, setting, hue } = location;
    await this.client.updateLocation({ locationId: id, name, setting: SETTINGS[setting], hue });
  }

  async deleteLocation(locationId: string): Promise<void> {
    await this.client.deleteLocation({ locationId });
  }

  async createProp(storyId: string, prop: Omit<Prop, 'id' | 'storyId'>): Promise<Prop> {
    const reply = await this.client.createProp({ storyId, name: prop.name });
    return toProp(required(reply.prop));
  }

  async updateProp(prop: Prop): Promise<void> {
    await this.client.updateProp({ propId: prop.id, name: prop.name });
  }

  async deleteProp(propId: string): Promise<void> {
    await this.client.deleteProp({ propId });
  }

  async saveVocabulary(storyId: string, vocabulary: StoryVocabulary): Promise<void> {
    await this.client.saveVocabulary({
      storyId,
      vocabulary: {
        timesOfDay: vocabulary.timesOfDay,
        transitions: vocabulary.transitions,
        characterKinds: vocabulary.characterKinds.map((k) => ({
          label: k.label,
          avatarShape: SHAPES[k.avatarShape],
        })),
      },
    });
  }
}

function required<T>(message: T | undefined): T {
  if (message === undefined) {
    throw new Error('The API returned an empty reply.');
  }
  return message;
}

/** The inverse of `values`; an unspecified or unknown member means the API and client disagree. */
function nameOf<K extends string, V>(values: Record<K, V>, value: V, noun: string): K {
  const name = (Object.keys(values) as K[]).find((k) => values[k] === value);
  if (name === undefined) {
    throw new Error(`The API sent an unknown ${noun}.`);
  }
  return name;
}

function toStorySummary(message: StorySummaryMessage): StorySummary {
  return {
    id: message.id,
    title: message.title,
    createdAt: new Date(Number(message.createdUnixSeconds) * 1000),
    lastModifiedAt: new Date(Number(message.lastModifiedUnixSeconds) * 1000),
  };
}

function toStory(message: StoryMessage): Story {
  return {
    id: message.id,
    title: message.title,
    vocabulary: toVocabulary(message.vocabulary),
    containers: message.containers.map(toContainer),
    characters: message.characters.map(toCharacter),
    locations: message.locations.map(toLocation),
    props: message.props.map(toProp),
  };
}

function toVocabulary(message: StoryVocabularyMessage | undefined): StoryVocabulary {
  return {
    timesOfDay: [...(message?.timesOfDay ?? [])],
    transitions: [...(message?.transitions ?? [])],
    characterKinds: (message?.characterKinds ?? []).map((k) => ({
      label: k.label,
      avatarShape: nameOf(SHAPES, k.avatarShape, 'avatar shape'),
    })),
  };
}

function toContainer(message: ContainerMessage): Container {
  return {
    id: message.id,
    storyId: message.storyId,
    position: message.position,
    label: message.label,
    scenes: message.scenes.map(toSceneSummary),
  };
}

function toSceneSummary(message: SceneSummaryMessage): SceneSummary {
  return {
    id: message.id,
    containerId: message.containerId,
    position: message.position,
    title: message.title,
    openingLocationId: message.openingLocationId ?? null,
    openingTimeOfDay: message.openingTimeOfDay ?? null,
  };
}

function toScene(message: SceneMessage): Scene {
  return {
    id: message.id,
    containerId: message.containerId,
    position: message.position,
    title: message.title,
    elements: message.elements.map(toElement),
  };
}

function toElement(message: SceneElementMessage): SceneElement {
  return {
    id: message.id,
    sceneId: message.sceneId,
    position: message.position,
    type: nameOf(ELEMENT_TYPES, message.type, 'row type'),
    text: message.text ?? null,
    locationId: message.locationId ?? null,
    timeOfDay: message.timeOfDay ?? null,
    speakerId: message.speakerId ?? null,
    parenthetical: message.parenthetical ?? null,
    transition: message.transition ?? null,
    mentions: message.mentions.map(toMention),
  };
}

function toMention(message: SceneElementMentionMessage): SceneElementMention {
  const { target } = message;
  return {
    id: message.id,
    characterId: target.case === 'characterId' ? target.value : null,
    locationId: target.case === 'locationId' ? target.value : null,
    propId: target.case === 'propId' ? target.value : null,
    offset: message.offset,
    length: message.length,
  };
}

function toCharacter(message: CharacterMessage): Character {
  return {
    id: message.id,
    storyId: message.storyId,
    name: message.name,
    kind: message.kind,
    hue: message.hue,
  };
}

function toLocation(message: LocationMessage): Location {
  return {
    id: message.id,
    storyId: message.storyId,
    name: message.name,
    setting: nameOf(SETTINGS, message.setting, 'location setting'),
    hue: message.hue,
  };
}

function toProp(message: PropMessage): Prop {
  return { id: message.id, storyId: message.storyId, name: message.name };
}

function fromScene(scene: Scene) {
  return {
    id: scene.id,
    containerId: scene.containerId,
    position: scene.position,
    title: scene.title,
    elements: scene.elements.map((e, position) => ({
      id: e.id,
      sceneId: scene.id,
      position,
      type: ELEMENT_TYPES[e.type],
      text: e.text ?? undefined,
      locationId: e.locationId ?? undefined,
      timeOfDay: e.timeOfDay ?? undefined,
      speakerId: e.speakerId ?? undefined,
      parenthetical: e.parenthetical ?? undefined,
      transition: e.transition ?? undefined,
      mentions: e.mentions.map(fromMention),
    })),
  };
}

function fromMention(mention: SceneElementMention) {
  return {
    id: mention.id,
    target: mention.characterId
      ? { case: 'characterId' as const, value: mention.characterId }
      : mention.locationId
        ? { case: 'locationId' as const, value: mention.locationId }
        : mention.propId
          ? { case: 'propId' as const, value: mention.propId }
          : { case: undefined },
    offset: mention.offset,
    length: mention.length,
  };
}
