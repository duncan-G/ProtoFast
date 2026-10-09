import { CueAudio } from './cue-audio';

/** How a dialogue, narration or action line is delivered. */
export interface Cue {
  /** Empty until the API has saved it. */
  id: string;
  elementId: string;
  /** The line's own words with delivery tags, e.g. "[sad] I'm sorry." The words never change. */
  performance: string;
  /** Minimum silence before the line, in milliseconds; negative overlaps the previous one. */
  gapBeforeMs: number;
  /** Cut off by the next line. */
  interrupted: boolean;
  /** Who reads an action or narration line; null is the story's first narrator. */
  narratorId: string | null;
  edited: boolean;
  /** Null until the line has been spoken. */
  audio: CueAudio | null;
}
