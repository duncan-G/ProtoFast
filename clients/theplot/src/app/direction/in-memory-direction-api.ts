import { Injectable } from '@angular/core';
import { Character } from '../stories/model/character';
import { Location } from '../stories/model/location';
import { Prop } from '../stories/model/prop';
import { Scene } from '../stories/model/scene';
import { StoryApi } from '../stories/story-api';
import type { DirectionApi } from './direction-api';
import { DescribeTarget } from './model/describe-target';
import { DirectTarget } from './model/direct-target';
import { emptyDirection, SceneDirection } from './model/scene-direction';
import { SpeechVoice } from './model/speech-voice';
import { isSpoken } from './direction-changes';
import { Cue } from './model/cue';
import { renumber } from './frames';

/**
 * Keeps the API's rules in memory: rows are matched by their place in the scene, changed rows are
 * marked edited, and ids are assigned on save. Directing writes plain cues and one frame over
 * the scene, standing in for the model.
 */
@Injectable({ providedIn: 'root' })
export class InMemoryDirectionApi implements DirectionApi {
  private readonly directions = new Map<string, SceneDirection>();

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

  async direct(sceneId: string, target: DirectTarget): Promise<SceneDirection> {
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
    if ((target.kind === 'scene' || target.kind === 'frames') && scene.elements.length > 0) {
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
          holdMs: null,
          placements: [],
          props: [],
          edited: false,
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
    return this.store(scene, current, false);
  }

  /** Every spoken line gets a silent data URL, so the player has something to play. */
  async generateAudio(sceneId: string, elementId: string | null = null): Promise<SceneDirection> {
    const scene = await this.stories.getScene(sceneId);
    const current = structuredClone(this.directions.get(sceneId) ?? emptyDirection(sceneId));
    for (const element of scene.elements) {
      if (isSpoken(element.type) && !current.cues.some((c) => c.elementId === element.id)) {
        current.cues.push({ ...plainCue(element.id, element.text ?? ''), id: crypto.randomUUID() });
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

  async listVoices(): Promise<{ provider: string; voices: SpeechVoice[] }> {
    return { provider: 'memory', voices: [{ id: 'default', name: 'Default' }] };
  }

  async describe(target: DescribeTarget): Promise<Character | Location | Prop> {
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
              JSON.stringify({ ...was, id: '', edited: false, position: 0 }) !==
                JSON.stringify({ ...p, id: '', edited: false, position: 0 });
            return {
              ...p,
              id: was?.id ?? crypto.randomUUID(),
              edited: changed ? byAuthor : (was?.edited ?? false),
            };
          }),
        scene.elements,
      ),
      appearances: direction.appearances.map((a) => {
        const was = stored.appearances.find((s) => s.characterId === a.characterId);
        const changed =
          !was ||
          JSON.stringify({ ...was, id: '', edited: false }) !==
            JSON.stringify({ ...a, id: '', edited: false });
        return {
          ...a,
          id: was?.id ?? crypto.randomUUID(),
          edited: changed ? byAuthor : (was?.edited ?? false),
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
    };
    this.directions.set(scene.id, structuredClone(next));
    return structuredClone(next);
  }

  private async storyOf(entryId: string) {
    for (const summary of await this.stories.listStories()) {
      const story = await this.stories.getStory(summary.id);
      if (
        story.characters.some((c) => c.id === entryId) ||
        story.locations.some((l) => l.id === entryId) ||
        story.props.some((p) => p.id === entryId)
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
