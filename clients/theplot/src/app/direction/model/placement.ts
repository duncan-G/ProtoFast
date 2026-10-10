/** A character in one frame; empty fields follow the scene. */
export interface Placement {
  characterId: string;
  /** left | centre | right | off, or empty */
  screen: string;
  /** foreground | midground | background, or empty */
  depth: string;
  /** Where in the place they are, in the place's own terms: "at the nurse station counter". */
  mark: string;
  /** A `DIRECTION_VOCABULARY.facings` label, or empty: as the viewer sees the picture. */
  facing: string;
  /** A `DIRECTION_VOCABULARY.movings` label, or empty. */
  moving: string;
  /** Who or what they look at. */
  looking: string;
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
    depth: '',
    mark: '',
    facing: '',
    moving: '',
    looking: '',
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
    !placement.depth &&
    !placement.mark &&
    !placement.facing &&
    !placement.moving &&
    !placement.looking &&
    !placement.pose &&
    !placement.expression &&
    placement.onScreen === null &&
    placement.wardrobeId === null &&
    !placement.wardrobeChange &&
    !placement.appearanceChange
  );
}
