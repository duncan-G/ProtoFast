import { MediaImage } from './media-image';

/** A character in the scene: whether frames show them, and their look as the scene opens. */
export interface SceneAppearance {
  id: string;
  characterId: string;
  /** On screen in every frame unless a frame says otherwise. */
  onScreen: boolean;
  /** left to right | right to left, or empty: the way they move across the screen all scene. */
  travel: string;
  /** Null wears the sheet's default, its first wardrobe. */
  wardrobeId: string | null;
  /** "hood up, damp from the drizzle" */
  wardrobeChange: string;
  /** "left hand bandaged" */
  appearanceChange: string;
  /** Prop ids they carry: in hand, under an arm, slung over a shoulder. */
  holding: string[];
  /** Why this differs from the character's previous look. */
  changeReason: string | null;
  changeElementId: string | null;
  edited: boolean;
  /** Null while the look is the library's, or until pictured. Read only: saves leave it alone. */
  image: MediaImage | null;
}

export function blankAppearance(characterId: string): SceneAppearance {
  return {
    id: '',
    characterId,
    onScreen: true,
    travel: '',
    wardrobeId: null,
    wardrobeChange: '',
    appearanceChange: '',
    holding: [],
    changeReason: null,
    changeElementId: null,
    edited: true,
    image: null,
  };
}
