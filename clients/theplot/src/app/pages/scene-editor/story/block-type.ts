import { SceneElementType } from '../../../stories/model/scene-element-type';

export interface BlockType {
  type: SceneElementType;
  label: string;
  hint: string;
  glyph: string;
}
