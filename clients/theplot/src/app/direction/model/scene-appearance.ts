/** A character in the scene: whether panels show them, and their look on top of the sheet. */
export interface SceneAppearance {
  id: string;
  characterId: string;
  /** On screen in every panel unless a panel says otherwise. */
  onScreen: boolean;
  /** Null wears the sheet's default, its first wardrobe. */
  wardrobeId: string | null;
  /** "hood up, damp from the drizzle" */
  wardrobeChange: string;
  /** "left hand bandaged" */
  appearanceChange: string;
  /** Why this differs from the character's previous look. */
  changeReason: string | null;
  changeElementId: string | null;
  edited: boolean;
}

export function blankAppearance(characterId: string): SceneAppearance {
  return {
    id: '',
    characterId,
    onScreen: true,
    wardrobeId: null,
    wardrobeChange: '',
    appearanceChange: '',
    changeReason: null,
    changeElementId: null,
    edited: true,
  };
}
