import { SceneElementType } from '../../../stories/model/scene-element-type';
import { BlockField } from './block-field';
import { BlockType } from './block-type';

/** Labels match the director view's gutter. */
export const BLOCK_TYPES: readonly BlockType[] = [
  { type: 'Action', label: 'Action', hint: 'What we see happen', glyph: '¶' },
  { type: 'Dialogue', label: 'Dialogue', hint: 'A character speaks', glyph: '“' },
  { type: 'Heading', label: 'Place', hint: 'Scene heading — INT. / EXT.', glyph: '⌖' },
  { type: 'Transition', label: 'Transition', hint: 'CUT TO:, DISSOLVE TO:', glyph: '→' },
  { type: 'Description', label: 'Description', hint: 'Look, light and texture', glyph: '◇' },
  { type: 'Narration', label: 'Narration', hint: 'A voice over the story', glyph: '~' },
];

/** Tab cycles these; ⌥1–4 picks one. */
export const CONVERTIBLE: readonly SceneElementType[] = [
  'Action',
  'Description',
  'Narration',
  'Dialogue',
];

export function blockLabel(type: SceneElementType): string {
  return BLOCK_TYPES.find((t) => t.type === type)?.label ?? type;
}

export function fieldsOf(type: SceneElementType): readonly BlockField[] {
  switch (type) {
    case 'Dialogue':
      return ['speaker', 'ext', 'paren', 'text'];
    case 'Heading':
      return ['setting', 'place', 'time'];
    case 'Transition':
      return ['trans'];
    default:
      return ['text'];
  }
}

/** Where the caret lands coming up from below. */
export function lastField(type: SceneElementType): BlockField {
  return type === 'Heading' ? 'place' : type === 'Transition' ? 'trans' : 'text';
}

/** Where the caret lands coming down from above. */
export function downField(type: SceneElementType): BlockField {
  return fieldsOf(type)[type === 'Heading' ? 1 : 0];
}

export const PLACEHOLDERS: Partial<Record<SceneElementType, string>> = {
  Action: 'Type / for blocks, or just write…',
  Description: 'Describe the look, the light, the texture…',
  Narration: 'Narrate…',
  Dialogue: 'What do they say?',
};
