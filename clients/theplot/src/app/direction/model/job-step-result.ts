import { Character } from '../../stories/model/character';
import { Extra } from '../../stories/model/extra';
import { Location } from '../../stories/model/location';
import { Prop } from '../../stories/model/prop';
import { FrameFinding } from './frame-finding';
import { LibraryImage } from './library-image';
import { SceneDirection } from './scene-direction';

/** A finished step's reply; a job keeps only its newest scene direction, so older ones are null. */
export type JobStepResult =
  | { kind: 'direct'; direction: SceneDirection | null; extras: Extra[]; findings: FrameFinding[] }
  | { kind: 'describe'; entry: Character | Location | Prop | Extra }
  | { kind: 'castVoice'; character: Character }
  | { kind: 'audio' | 'frameImage' | 'lookImage'; direction: SceneDirection | null }
  | { kind: 'libraryImage'; image: LibraryImage | null };
