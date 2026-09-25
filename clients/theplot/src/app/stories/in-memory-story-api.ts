import { Injectable } from '@angular/core';
import { forgetInElement, renameInElement } from './element-references';
import { nameProblem } from './library-names';
import type { StoryApi } from './story-api';
import { Character } from './model/character';
import { Container } from './model/container';
import { Location } from './model/location';
import { Prop } from './model/prop';
import { ReferenceTarget } from './model/reference-target';
import { Scene } from './model/scene';
import { SceneElement } from './model/scene-element';
import { SceneSummary } from './model/scene-summary';
import { Story } from './model/story';
import { StoryVocabulary } from './model/story-vocabulary';
import { sampleScenes, sampleStory } from './sample-story';

/** Keeps the API's rules: unique names, and renames and deletes reaching every scene. */
@Injectable({ providedIn: 'root' })
export class InMemoryStoryApi implements StoryApi {
  private readonly stories = new Map<string, Story>();
  private readonly scenes = new Map<string, Scene>();

  constructor() {
    const story = sampleStory();
    this.stories.set(story.id, story);
    for (const scene of sampleScenes()) {
      this.scenes.set(scene.id, scene);
    }
  }

  async getStory(storyId: string): Promise<Story> {
    const story = this.story(storyId);
    return structuredClone({
      ...story,
      containers: story.containers.map((c) => ({
        ...c,
        scenes: this.summaries(c.id),
      })),
    });
  }

  async getScene(sceneId: string): Promise<Scene> {
    return structuredClone(this.scene(sceneId));
  }

  async saveScene(scene: Scene): Promise<void> {
    const stored = this.scene(scene.id);
    const title = scene.title.trim() || 'Untitled scene';
    this.scenes.set(scene.id, {
      ...structuredClone(scene),
      containerId: stored.containerId,
      position: stored.position,
      title,
      elements: scene.elements.map((e, position) => ({
        ...structuredClone(e),
        sceneId: scene.id,
        position,
      })),
    });
  }

  async createScene(scene: Scene): Promise<Scene> {
    this.container(scene.containerId);
    const siblings = [...this.scenes.values()].filter((s) => s.containerId === scene.containerId);
    const position = Math.max(0, Math.min(scene.position, siblings.length));
    for (const sibling of siblings) {
      if (sibling.position >= position) {
        sibling.position++;
      }
    }
    const created = { ...structuredClone(scene), position };
    this.scenes.set(created.id, created);
    return structuredClone(created);
  }

  async createContainer(storyId: string, label: string): Promise<Container> {
    const story = this.story(storyId);
    const container: Container = {
      id: crypto.randomUUID(),
      storyId,
      position: story.containers.length,
      label: requireLabel(label),
      scenes: [],
    };
    story.containers.push(container);
    return structuredClone(container);
  }

  async renameContainer(containerId: string, label: string): Promise<void> {
    this.container(containerId).label = requireLabel(label);
  }

  async createCharacter(
    storyId: string,
    draft: Omit<Character, 'id' | 'storyId'>,
  ): Promise<Character> {
    const story = this.story(storyId);
    const character = {
      ...draft,
      id: crypto.randomUUID(),
      storyId,
      name: checkName(story.characters, draft.name, 'character'),
    };
    story.characters.push(character);
    return structuredClone(character);
  }

  async updateCharacter(character: Character): Promise<void> {
    const story = this.story(character.storyId);
    this.replaceEntry(story.characters, character, 'character');
  }

  async deleteCharacter(characterId: string): Promise<void> {
    this.deleteEntry((s) => s.characters, {
      kind: 'character',
      id: characterId,
    });
  }

  async createLocation(
    storyId: string,
    draft: Omit<Location, 'id' | 'storyId'>,
  ): Promise<Location> {
    const story = this.story(storyId);
    const location = {
      ...draft,
      id: crypto.randomUUID(),
      storyId,
      name: checkName(story.locations, draft.name, 'location'),
    };
    story.locations.push(location);
    return structuredClone(location);
  }

  async updateLocation(location: Location): Promise<void> {
    const story = this.story(location.storyId);
    this.replaceEntry(story.locations, location, 'location');
  }

  async deleteLocation(locationId: string): Promise<void> {
    this.deleteEntry((s) => s.locations, { kind: 'location', id: locationId });
  }

  async createProp(storyId: string, draft: Omit<Prop, 'id' | 'storyId'>): Promise<Prop> {
    const story = this.story(storyId);
    const prop = {
      ...draft,
      id: crypto.randomUUID(),
      storyId,
      name: checkName(story.props, draft.name, 'prop'),
    };
    story.props.push(prop);
    return structuredClone(prop);
  }

  async updateProp(prop: Prop): Promise<void> {
    const story = this.story(prop.storyId);
    this.replaceEntry(story.props, prop, 'prop');
  }

  async deleteProp(propId: string): Promise<void> {
    this.deleteEntry((s) => s.props, { kind: 'prop', id: propId });
  }

  async saveVocabulary(storyId: string, vocabulary: StoryVocabulary): Promise<void> {
    this.story(storyId).vocabulary = structuredClone(vocabulary);
  }

  private replaceEntry<T extends { id: string; storyId: string; name: string }>(
    list: T[],
    entry: T,
    kind: ReferenceTarget['kind'],
  ): void {
    const index = list.findIndex((e) => e.id === entry.id);
    if (index < 0) {
      throw new Error(`That ${kind} is no longer in the library.`);
    }
    const name = checkName(list, entry.name, kind, entry.id);
    if (name !== list[index].name) {
      this.updateScenesOf(entry.storyId, (e) => renameInElement(e, { kind, id: entry.id }, name));
    }
    list[index] = { ...structuredClone(entry), name };
  }

  private deleteEntry(list: (story: Story) => { id: string }[], target: ReferenceTarget): void {
    for (const story of this.stories.values()) {
      const entries = list(story);
      const index = entries.findIndex((e) => e.id === target.id);
      if (index >= 0) {
        entries.splice(index, 1);
        this.updateScenesOf(story.id, (e) => forgetInElement(e, target));
        return;
      }
    }
  }

  private updateScenesOf(storyId: string, update: (element: SceneElement) => SceneElement): void {
    const containerIds = new Set(this.story(storyId).containers.map((c) => c.id));
    for (const scene of this.scenes.values()) {
      if (containerIds.has(scene.containerId)) {
        scene.elements = scene.elements.map(update);
      }
    }
  }

  private summaries(containerId: string): SceneSummary[] {
    return [...this.scenes.values()]
      .filter((s) => s.containerId === containerId)
      .sort((a, b) => a.position - b.position)
      .map((s) => {
        const opening = s.elements.find((e) => e.type === 'Heading');
        return {
          id: s.id,
          containerId: s.containerId,
          position: s.position,
          title: s.title,
          openingLocationId: opening?.locationId ?? null,
          openingTimeOfDay: opening?.timeOfDay ?? null,
        };
      });
  }

  private story(storyId: string): Story {
    const story = this.stories.get(storyId);
    if (!story) {
      throw new Error('That story could not be found.');
    }
    return story;
  }

  private scene(sceneId: string): Scene {
    const scene = this.scenes.get(sceneId);
    if (!scene) {
      throw new Error('That scene could not be found.');
    }
    return scene;
  }

  private container(containerId: string): Container {
    for (const story of this.stories.values()) {
      const container = story.containers.find((c) => c.id === containerId);
      if (container) {
        return container;
      }
    }
    throw new Error('That part of the story could not be found.');
  }
}

function checkName<T extends { id: string; name: string }>(
  list: readonly T[],
  name: string,
  noun: string,
  exceptId?: string,
): string {
  const problem = nameProblem(list, name, noun, exceptId);
  if (problem) {
    throw new Error(problem);
  }
  return name.trim();
}

function requireLabel(label: string): string {
  const trimmed = label.trim();
  if (!trimmed) {
    throw new Error('A container needs a label.');
  }
  return trimmed;
}
