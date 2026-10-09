import { LocationLighting } from './location-lighting';

export interface LocationAppearance {
  layout: string;
  keyFeatures: string;
  lighting: LocationLighting[];
}
