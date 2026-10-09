import { CharacterKind } from './character-kind';

/** The labels a story adds to `DEFAULT_VOCABULARY`. */
export interface StoryVocabulary {
  timesOfDay: string[];
  transitions: string[];
  extensions: string[];
  characterKinds: CharacterKind[];
  /** Delivery tags beyond `DIRECTION_VOCABULARY.deliveryTags`, lowercase. */
  deliveryTags: string[];
}
