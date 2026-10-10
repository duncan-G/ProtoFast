import { Extra } from '../../stories/model/extra';
import { FrameFinding } from './frame-finding';
import { SceneDirection } from './scene-direction';

/** A directing pass's result, with the story's extras, which it may have added to. */
export interface DirectResult {
  direction: SceneDirection;
  extras: Extra[];
  findings: FrameFinding[];
}
