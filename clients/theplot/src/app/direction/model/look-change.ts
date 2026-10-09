import { MediaImage } from './media-image';

/**
 * A character's whole look from one line of the scene on: the frame covering the line and every
 * frame after it show it, and the next scene opens with the last one.
 */
export interface LookChange {
  /** Empty until the API has saved it. */
  id: string;
  characterId: string;
  elementId: string;
  /** Null wears the sheet's default, its first wardrobe. */
  wardrobeId: string | null;
  wardrobeChange: string;
  appearanceChange: string;
  /** Prop ids they carry from this line on. */
  holding: string[];
  /** "hands the cooler to the doctor" */
  reason: string | null;
  edited: boolean;
  /** Null while the look is the library's, or until pictured. Read only: saves leave it alone. */
  image: MediaImage | null;
}
