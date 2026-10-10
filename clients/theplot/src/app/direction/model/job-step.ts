import { DescribeTarget } from './describe-target';
import { DirectTarget } from './direct-target';
import { PictureRefinement } from './picture-refinement';
import { PictureTarget } from './picture-target';

/** One Direction request a background job makes; a refinement edits the current picture instead of drawing it. */
export type JobStep =
  | { kind: 'direct'; sceneId: string; target: DirectTarget; replaceEdited: boolean }
  | { kind: 'describe'; target: DescribeTarget }
  | { kind: 'castVoice'; characterId: string }
  | { kind: 'audio'; sceneId: string; elementId: string; regenerate: boolean }
  | {
      kind: 'frameImage';
      sceneId: string;
      frameId: string;
      regenerate: boolean;
      refinement: PictureRefinement | null;
    }
  | {
      kind: 'lookImage';
      sceneId: string;
      characterId: string;
      elementId: string | null;
      regenerate: boolean;
      refinement: PictureRefinement | null;
    }
  | {
      kind: 'libraryImage';
      target: PictureTarget;
      regenerate: boolean;
      refinement: PictureRefinement | null;
    };
