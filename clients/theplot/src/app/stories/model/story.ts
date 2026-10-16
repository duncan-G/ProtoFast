import { Character } from './character';
import { Container } from './container';
import { Location } from './location';
import { Prop } from './prop';
import { StorySpot } from './story-spot';
import { StoryVocabulary } from './story-vocabulary';

/** Scenes' elements load separately, with `StoryApi.getScene`. */
export interface Story {
  id: string;
  title: string;
  vocabulary: StoryVocabulary;
  containers: Container[];
  characters: Character[];
  locations: Location[];
  props: Prop[];
  spot: StorySpot | null;
}
