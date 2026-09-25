import { SceneElementMention } from './scene-element-mention';
import { SceneElementType } from './scene-element-type';

/** A row in a scene. `type` decides which of the nullable fields are set. */
export interface SceneElement {
  id: string;
  sceneId: string;
  position: number;
  type: SceneElementType;
  text: string | null;
  locationId: string | null;
  timeOfDay: string | null;
  speakerId: string | null;
  /** Without the parentheses. */
  parenthetical: string | null;
  transition: string | null;
  mentions: SceneElementMention[];
}
