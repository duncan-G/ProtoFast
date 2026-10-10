/** An extra a frame shows, hides or stages against the scene. */
export interface FrameExtra {
  extraId: string;
  /** Null follows the scene. */
  onScreen: boolean | null;
  /** As on a placement; `pose` says which of a group the staging is about. */
  screen: string;
  facing: string;
  moving: string;
  looking: string;
  pose: string;
}

export function blankFrameExtra(extraId: string): FrameExtra {
  return { extraId, onScreen: null, screen: '', facing: '', moving: '', looking: '', pose: '' };
}

/** A row that says nothing the scene does not. */
export function isBlankFrameExtra(extra: FrameExtra): boolean {
  return (
    extra.onScreen === null &&
    !extra.screen &&
    !extra.facing &&
    !extra.moving &&
    !extra.looking &&
    !extra.pose
  );
}
