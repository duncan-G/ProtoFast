import { FrameProp } from './frame-prop';
import { Placement } from './placement';

/** One picture of the scene, covering a contiguous run of its elements. Frames do not overlap. */
export interface Frame {
  /** Empty until the API has saved it. */
  id: string;
  position: number;
  firstElementId: string;
  lastElementId: string;
  locationId: string | null;
  timeOfDay: string | null;
  /** What is visible: an action, or "MARA presses, TOM deflects". */
  beat: string;
  /** Null takes the scene's. */
  shot: string | null;
  angle: string | null;
  movement: string | null;
  /** What this frame adds to the location's look in the scene. */
  locationChange: string;
  /** Only for frames with no dialogue to time them. */
  holdMs: number | null;
  /** Only the characters the frame says something about; the rest follow the scene. */
  placements: Placement[];
  /** Only the props the frame shows or hides against the scene. */
  props: FrameProp[];
  /** Written by hand; a directing pass leaves it alone. */
  edited: boolean;
}
