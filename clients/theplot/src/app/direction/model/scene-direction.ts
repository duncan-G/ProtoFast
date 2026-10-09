import { Cue } from './cue';
import { Frame } from './frame';
import { SceneAppearance } from './scene-appearance';
import { SceneLocationState } from './scene-location-state';
import { ScenePropState } from './scene-prop-state';

/** The director layer over a scene. */
export interface SceneDirection {
  sceneId: string;
  /** Which day of the story the scene happens on; looks reset between days. */
  storyDay: number | null;
  /** Every frame's unless it sets its own. */
  shot: string | null;
  angle: string | null;
  movement: string | null;
  /** In position order. */
  frames: Frame[];
  cues: Cue[];
  appearances: SceneAppearance[];
  propStates: ScenePropState[];
  locationStates: SceneLocationState[];
}

export function emptyDirection(sceneId: string): SceneDirection {
  return {
    sceneId,
    storyDay: null,
    shot: null,
    angle: null,
    movement: null,
    frames: [],
    cues: [],
    appearances: [],
    propStates: [],
    locationStates: [],
  };
}

/** Whether the author or the model has directed anything in the scene yet. */
export function isDirected(direction: SceneDirection): boolean {
  return (
    direction.storyDay !== null ||
    direction.shot !== null ||
    direction.frames.length > 0 ||
    direction.cues.length > 0 ||
    direction.appearances.length > 0 ||
    direction.propStates.length > 0 ||
    direction.locationStates.length > 0
  );
}
