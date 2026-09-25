import { SceneElement } from '../../stories/model/scene-element';

export interface SceneRow {
  element: SceneElement;
  index: number;
  /** The hue of the heading above. */
  hue: number | null;
  /** On a heading: the beats under it. */
  beats: number;
}
