import { Injectable } from '@angular/core';
import { Character } from '../stories/model/character';
import { Extra } from '../stories/model/extra';
import { Location } from '../stories/model/location';
import { Prop } from '../stories/model/prop';
import { Scene } from '../stories/model/scene';
import { EMPTY_VOICE } from '../stories/model/voice-profile';
import { StoryApi } from '../stories/story-api';
import type { DirectionApi } from './direction-api';
import { DescribeTarget } from './model/describe-target';
import { DirectResult } from './model/direct-result';
import { DirectTarget } from './model/direct-target';
import { LibraryImage } from './model/library-image';
import { MediaImage } from './model/media-image';
import { emptyDirection, SceneDirection } from './model/scene-direction';
import { SpeechVoice } from './model/speech-voice';
import { VoiceQuery } from './model/voice-query';
import { isSpoken } from './direction-changes';
import { Cue } from './model/cue';
import { renumber } from './frames';

const MEMORY_VOICES: SpeechVoice[] = [
  {
    id: 'narrator',
    name: 'Narrator',
    description: 'Warm and unhurried.',
    traits: 'female · middle aged · british',
    previewUrl: '',
  },
  {
    id: 'gravel',
    name: 'Gravel',
    description: 'Low and rough.',
    traits: 'male · old · american',
    previewUrl: '',
  },
];

/**
 * Keeps the API's rules in memory: rows are matched by their place in the scene, changed rows are
 * marked edited, and ids are assigned on save. Directing writes plain cues and one frame over
 * the scene, standing in for the model.
 */
@Injectable({ providedIn: 'root' })
export class InMemoryDirectionApi implements DirectionApi {
  private readonly directions = new Map<string, SceneDirection>();
  private readonly libraryImages = new Map<string, LibraryImage>();

  constructor(private readonly stories: StoryApi) {}

  async getSceneDirection(sceneId: string): Promise<SceneDirection> {
    return structuredClone(this.directions.get(sceneId) ?? emptyDirection(sceneId));
  }

  async saveSceneDirection(direction: SceneDirection): Promise<SceneDirection> {
    const scene = await this.stories.getScene(direction.sceneId);
    return this.store(scene, direction, true);
  }

  async saveDirectedScene(scene: Scene, direction: SceneDirection): Promise<SceneDirection> {
    await this.stories.saveScene(scene);
    return this.store(await this.stories.getScene(scene.id), direction, true);
  }

  async direct(sceneId: string, target: DirectTarget): Promise<DirectResult> {
    const scene = await this.stories.getScene(sceneId);
    const current = structuredClone(this.directions.get(sceneId) ?? emptyDirection(sceneId));
    if (target.kind === 'scene' || target.kind === 'cues' || target.kind === 'cue') {
      for (const element of scene.elements) {
        if (!isSpoken(element.type)) {
          continue;
        }
        if (target.kind === 'cue' && element.id !== target.elementId) {
          continue;
        }
        const existing = current.cues.find((c) => c.elementId === element.id);
        if (existing?.edited && target.kind !== 'cue') {
          continue;
        }
        current.cues = current.cues.filter((c) => c.elementId !== element.id);
        current.cues.push({
          ...plainCue(element.id, element.text ?? ''),
          performance:
            element.type === 'Dialogue'
              ? `[calm] ${element.text ?? ''}`.trim()
              : (element.text ?? ''),
          narratorId: existing?.narratorId ?? null,
        });
      }
    }
    if (
      (target.kind === 'scene' || target.kind === 'frames') &&
      scene.elements.length > 0 &&
      !current.frames.some((f) => f.locked)
    ) {
      const heading = scene.elements.find((e) => e.type === 'Heading');
      current.frames = [
        {
          id: '',
          position: 0,
          firstElementId: scene.elements[0].id,
          lastElementId: scene.elements[scene.elements.length - 1].id,
          locationId: heading?.locationId ?? null,
          timeOfDay: heading?.timeOfDay ?? null,
          beat: 'The whole scene.',
          shot: null,
          angle: null,
          movement: null,
          locationChange: '',
          camera: '',
          variation: '',
          holdMs: null,
          placements: [],
          props: [],
          extras: [],
          edited: false,
          locked: false,
          image: null,
        },
      ];
    }
    if (target.kind === 'scene' || target.kind === 'frames') {
      current.shot = current.shot ?? 'wide';
      current.angle = current.angle ?? 'eye level';
      current.movement = current.movement ?? 'static';
    }
    if (target.kind === 'scene' || target.kind === 'storyDay') {
      current.storyDay = current.storyDay ?? 1;
    }
    return {
      direction: this.store(scene, current, false),
      extras: (await this.sceneStory(sceneId))?.extras ?? [],
    };
  }

  private async sceneStory(sceneId: string) {
    const scene = await this.stories.getScene(sceneId);
    for (const summary of await this.stories.listStories()) {
      const story = await this.stories.getStory(summary.id);
      if (story.containers.some((c) => c.id === scene.containerId)) {
        return story;
      }
    }
    return null;
  }

  /** Every spoken line gets a silent data URL, so the player has something to play. */
  async generateAudio(sceneId: string, elementId: string | null = null): Promise<SceneDirection> {
    const scene = await this.stories.getScene(sceneId);
    const current = structuredClone(this.directions.get(sceneId) ?? emptyDirection(sceneId));
    for (const element of scene.elements) {
      if (isSpoken(element.type) && !current.cues.some((c) => c.elementId === element.id)) {
        current.cues.push({
          ...plainCue(element.id, element.text ?? ''),
          id: crypto.randomUUID(),
        });
      }
    }
    for (const cue of current.cues) {
      if (elementId === null || cue.elementId === elementId) {
        cue.audio = {
          assetId: crypto.randomUUID(),
          url: 'data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAAB9AAACABAAZGF0YQAAAAA=',
          contentType: 'audio/wav',
          stale: false,
          voice: 'default',
        };
      }
    }
    this.directions.set(sceneId, structuredClone(current));
    return current;
  }

  async searchVoices(query: VoiceQuery): Promise<SpeechVoice[]> {
    const search = query.search.trim().toLowerCase();
    return MEMORY_VOICES.filter(
      (v) =>
        (!search || v.name.toLowerCase().includes(search)) &&
        [query.gender, query.age.replace('_', ' '), query.accent].every(
          (label) => !label || v.traits.includes(label),
        ),
    );
  }

  async castVoice(characterId: string): Promise<Character> {
    const story = await this.storyOf(characterId);
    const character = story.characters.find((c) => c.id === characterId)!;
    const voice = MEMORY_VOICES.find((v) => v.id !== character.voice?.voiceId) ?? MEMORY_VOICES[0];
    return this.stories.updateCharacterSheet({
      ...character,
      voice: {
        ...(character.voice ?? EMPTY_VOICE),
        voiceId: voice.id,
        voiceName: voice.name,
        previewUrl: voice.previewUrl,
      },
    });
  }

  async describe(target: DescribeTarget): Promise<Character | Location | Prop | Extra> {
    const story = await this.storyOf(target.id);
    switch (target.kind) {
      case 'character': {
        const character = story.characters.find((c) => c.id === target.id)!;
        return this.stories.updateCharacterSheet({
          ...character,
          description: `${character.name}, as the story shows them.`,
        });
      }
      case 'location': {
        const location = story.locations.find((l) => l.id === target.id)!;
        return this.stories.updateLocationSheet({
          ...location,
          description: `${location.name}, as the story shows it.`,
        });
      }
      case 'prop': {
        const prop = story.props.find((p) => p.id === target.id)!;
        return this.stories.updatePropSheet({
          ...prop,
          description: `${prop.name}, as the story shows it.`,
        });
      }
      case 'extra': {
        const extra = story.extras.find((e) => e.id === target.id)!;
        return this.stories.updateExtra({
          ...extra,
          description: `${extra.name}, as the story shows them.`,
        });
      }
    }
  }

  private store(scene: Scene, direction: SceneDirection, byAuthor: boolean): SceneDirection {
    const stored = this.directions.get(scene.id) ?? emptyDirection(scene.id);
    const ids = new Set(scene.elements.map((e) => e.id));
    const next: SceneDirection = {
      sceneId: scene.id,
      storyDay: direction.storyDay,
      shot: direction.shot,
      angle: direction.angle,
      movement: direction.movement,
      cues: direction.cues
        .filter((c) => ids.has(c.elementId))
        .map((c) => {
          const was = stored.cues.find((s) => s.elementId === c.elementId);
          const changed =
            !was ||
            JSON.stringify({ ...was, id: '', edited: false }) !==
              JSON.stringify({ ...c, id: '', edited: false });
          return {
            ...c,
            id: was?.id ?? crypto.randomUUID(),
            edited: changed ? byAuthor : (was?.edited ?? false),
          };
        }),
      frames: renumber(
        direction.frames
          .filter((p) => ids.has(p.firstElementId) && ids.has(p.lastElementId))
          .map((p) => {
            const was = stored.frames.find((s) => s.firstElementId === p.firstElementId);
            const changed =
              !was ||
              JSON.stringify({
                ...was,
                id: '',
                edited: false,
                locked: false,
                position: 0,
                image: null,
              }) !==
                JSON.stringify({
                  ...p,
                  id: '',
                  edited: false,
                  locked: false,
                  position: 0,
                  image: null,
                });
            return {
              ...p,
              id: was?.id ?? crypto.randomUUID(),
              edited: changed ? byAuthor : (was?.edited ?? false),
              image: was?.image ?? null,
            };
          }),
        scene.elements,
      ),
      appearances: direction.appearances.map((a) => {
        const was = stored.appearances.find((s) => s.characterId === a.characterId);
        const changed =
          !was ||
          JSON.stringify({ ...was, id: '', edited: false, image: null }) !==
            JSON.stringify({ ...a, id: '', edited: false, image: null });
        return {
          ...a,
          id: was?.id ?? crypto.randomUUID(),
          edited: changed ? byAuthor : (was?.edited ?? false),
          image: was?.image ?? null,
        };
      }),
      lookChanges: direction.lookChanges
        .filter((c) => ids.has(c.elementId))
        .map((c) => {
          const was = stored.lookChanges.find(
            (s) => s.characterId === c.characterId && s.elementId === c.elementId,
          );
          const changed =
            !was ||
            JSON.stringify({ ...was, id: '', edited: false, image: null }) !==
              JSON.stringify({ ...c, id: '', edited: false, image: null });
          return {
            ...c,
            id: was?.id ?? crypto.randomUUID(),
            edited: changed ? byAuthor : (was?.edited ?? false),
            image: was?.image ?? null,
          };
        }),
      propStates: direction.propStates.map((p) => {
        const was = stored.propStates.find((s) => s.propId === p.propId);
        const changed =
          !was ||
          JSON.stringify({ ...was, id: '', edited: false }) !==
            JSON.stringify({ ...p, id: '', edited: false });
        return {
          ...p,
          id: was?.id ?? crypto.randomUUID(),
          edited: changed ? byAuthor : (was?.edited ?? false),
        };
      }),
      locationStates: direction.locationStates.map((l) => {
        const was = stored.locationStates.find((s) => s.locationId === l.locationId);
        const changed = !was || was.change !== l.change;
        return {
          ...l,
          id: was?.id ?? crypto.randomUUID(),
          edited: changed ? byAuthor : (was?.edited ?? false),
        };
      }),
      extras: direction.extras.map((e) => {
        const was = stored.extras.find((s) => s.extraId === e.extraId);
        const changed = !was || was.onScreen !== e.onScreen;
        return {
          ...e,
          id: was?.id ?? crypto.randomUUID(),
          edited: changed ? byAuthor : (was?.edited ?? false),
        };
      }),
    };
    this.directions.set(scene.id, structuredClone(next));
    return structuredClone(next);
  }

  async generateFrameImage(sceneId: string, frameId: string): Promise<SceneDirection> {
    const current = structuredClone(this.directions.get(sceneId) ?? emptyDirection(sceneId));
    const frame = current.frames.find((f) => f.id === frameId);
    if (!frame) {
      throw new Error('That frame is no longer in the scene.');
    }
    if (frame.locked) {
      return current;
    }
    frame.image = placeholderImage(frame.beat || `Frame ${frame.position + 1}`);
    this.directions.set(sceneId, structuredClone(current));
    return current;
  }

  async generateLookImage(
    sceneId: string,
    characterId: string,
    elementId: string | null,
  ): Promise<SceneDirection> {
    const current = structuredClone(this.directions.get(sceneId) ?? emptyDirection(sceneId));
    const row =
      elementId === null
        ? current.appearances.find((a) => a.characterId === characterId)
        : current.lookChanges.find(
            (c) => c.characterId === characterId && c.elementId === elementId,
          );
    if (!row) {
      throw new Error('That look is no longer in the scene.');
    }
    row.image = placeholderImage(row.appearanceChange || 'Look');
    this.directions.set(sceneId, structuredClone(current));
    return current;
  }

  async generateLibraryImage(target: DescribeTarget): Promise<LibraryImage | null> {
    const story = await this.storyOf(target.id);
    const name =
      [...story.characters, ...story.locations, ...story.props].find((e) => e.id === target.id)
        ?.name ?? '';
    const image: LibraryImage = { target, image: placeholderImage(name) };
    this.libraryImages.set(target.id, image);
    return structuredClone(image);
  }

  async listLibraryImages(storyId: string): Promise<LibraryImage[]> {
    const story = await this.stories.getStory(storyId);
    const ids = new Set([...story.characters, ...story.locations, ...story.props].map((e) => e.id));
    return structuredClone([...this.libraryImages.values()].filter((i) => ids.has(i.target.id)));
  }

  private async storyOf(entryId: string) {
    for (const summary of await this.stories.listStories()) {
      const story = await this.stories.getStory(summary.id);
      if (
        story.characters.some((c) => c.id === entryId) ||
        story.locations.some((l) => l.id === entryId) ||
        story.props.some((p) => p.id === entryId) ||
        story.extras.some((e) => e.id === entryId)
      ) {
        return story;
      }
    }
    throw new Error('That entry is no longer in the library.');
  }
}

function plainCue(elementId: string, text: string): Cue {
  return {
    id: '',
    elementId,
    performance: text.replace(/@/g, '').trim(),
    gapBeforeMs: 0,
    interrupted: false,
    narratorId: null,
    edited: false,
    audio: null,
  };
}

/** A grey card with its subject written on it, standing in for the image model. */
function placeholderImage(label: string): MediaImage {
  const text = label.replace(/[<&"]/g, ' ').slice(0, 40);
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 300 200"><rect width="300" height="200" fill="#57534e"/><text x="150" y="105" fill="#e7e5e4" font-family="serif" font-size="16" text-anchor="middle">${text}</text></svg>`;
  return {
    assetId: crypto.randomUUID(),
    url: `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`,
    contentType: 'image/svg+xml',
    stale: false,
    references: [],
    notes: '',
  };
}
