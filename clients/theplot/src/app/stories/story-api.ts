import { Injectable } from '@angular/core';
import { ConnectStoryApi } from './connect-story-api';
import { Character } from './model/character';
import { Container } from './model/container';
import { Location } from './model/location';
import { Prop } from './model/prop';
import { Scene } from './model/scene';
import { Story } from './model/story';
import { StorySummary } from './model/story-summary';
import { StoryVocabulary } from './model/story-vocabulary';

/** Names are unique per kind, ignoring case; a rename rewrites its `@Name`s, a delete unlinks them. */
@Injectable({ providedIn: 'root', useClass: ConnectStoryApi })
export abstract class StoryApi {
  /** Most recently modified first. */
  abstract listStories(): Promise<StorySummary[]>;

  /** Starts with "Act I" holding one untitled scene, as `getStory` returns it. */
  abstract createStory(title: string): Promise<Story>;

  abstract updateStory(storyId: string, title: string): Promise<void>;

  abstract deleteStory(storyId: string): Promise<void>;

  abstract getStory(storyId: string): Promise<Story>;

  abstract getScene(sceneId: string): Promise<Scene>;

  /** Positions follow the order of `scene.elements`. */
  abstract saveScene(scene: Scene): Promise<void>;

  /** Inserts at `scene.position`, moving later scenes down one. */
  abstract createScene(scene: Scene): Promise<Scene>;

  abstract createContainer(storyId: string, label: string): Promise<Container>;

  abstract renameContainer(containerId: string, label: string): Promise<void>;

  abstract createCharacter(
    storyId: string,
    character: Omit<Character, 'id' | 'storyId'>,
  ): Promise<Character>;

  abstract updateCharacter(character: Character): Promise<void>;

  abstract deleteCharacter(characterId: string): Promise<void>;

  abstract createLocation(
    storyId: string,
    location: Omit<Location, 'id' | 'storyId'>,
  ): Promise<Location>;

  abstract updateLocation(location: Location): Promise<void>;

  abstract deleteLocation(locationId: string): Promise<void>;

  abstract createProp(storyId: string, prop: Omit<Prop, 'id' | 'storyId'>): Promise<Prop>;

  abstract updateProp(prop: Prop): Promise<void>;

  abstract deleteProp(propId: string): Promise<void>;

  abstract saveVocabulary(storyId: string, vocabulary: StoryVocabulary): Promise<void>;
}
