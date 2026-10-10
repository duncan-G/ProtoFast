import { Acoustics } from './acoustics';
import { LocationAppearance } from './location-appearance';
import { LocationSetting } from './location-setting';
import { LocationVariation } from './location-variation';

export interface Location {
  id: string;
  storyId: string;
  name: string;
  setting: LocationSetting;
  /** OKLCH hue, 0–359. */
  hue: number;
  description: string | null;
  appearance: LocationAppearance | null;
  acoustics: Acoustics | null;
  /** Parts of the place the camera moves to, in order. */
  variations: LocationVariation[];
}
