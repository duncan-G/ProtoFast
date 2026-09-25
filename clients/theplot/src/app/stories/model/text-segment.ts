import { SceneElementMention } from './scene-element-mention';

export interface TextSegment {
  text: string;
  mention: SceneElementMention | null;
}
