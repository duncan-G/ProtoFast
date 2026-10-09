import { Character } from './character';
import { Container } from './container';
import { Location } from './location';
import { Prop } from './prop';
import { Extra } from './extra';
import { StoryVocabulary } from './story-vocabulary';
import { Narrator } from './narrator';

/** Scenes' elements load separately, with `StoryApi.getScene`. */
export interface Story {
  id: string;
  title: string;
  vocabulary: StoryVocabulary;
  containers: Container[];
  characters: Character[];
  locations: Location[];
  props: Prop[];
  /** Who reads action and narration, in order; the first is the default. */
  narrators: Narrator[];
  extras: Extra[];
}
