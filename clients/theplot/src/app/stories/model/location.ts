import { Acoustics } from './acoustics';
import { LocationAppearance } from './location-appearance';
import { LocationSetting } from './location-setting';

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
}
