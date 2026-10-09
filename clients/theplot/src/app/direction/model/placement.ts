/** A character in one frame; empty fields follow the scene. */
export interface Placement {
  characterId: string;
  /** left | centre | right | off, or empty */
  screen: string;
  pose: string;
  expression: string;
  /** Null follows the scene's appearance. */
  onScreen: boolean | null;
  /** Null wears the scene's wardrobe. */
  wardrobeId: string | null;
  /** Added to the scene's changes, never replacing them. */
  wardrobeChange: string;
  appearanceChange: string;
}

export function blankPlacement(characterId: string): Placement {
  return {
    characterId,
    screen: '',
    pose: '',
    expression: '',
    onScreen: null,
    wardrobeId: null,
    wardrobeChange: '',
    appearanceChange: '',
  };
}

/** A placement that says nothing the scene does not. */
export function isBlankPlacement(placement: Placement): boolean {
  return (
    !placement.screen &&
    !placement.pose &&
    !placement.expression &&
    placement.onScreen === null &&
    placement.wardrobeId === null &&
    !placement.wardrobeChange &&
    !placement.appearanceChange
  );
}
