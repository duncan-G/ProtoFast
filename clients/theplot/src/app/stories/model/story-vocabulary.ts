import { CharacterKind } from './character-kind';

/** The labels a story adds to `DEFAULT_VOCABULARY`. */
export interface StoryVocabulary {
  timesOfDay: string[];
  transitions: string[];
  characterKinds: CharacterKind[];
}
