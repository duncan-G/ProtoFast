import { StoryVocabulary } from './story-vocabulary';

/** Mirrors the API's `DefaultVocabulary`. */
export const DEFAULT_VOCABULARY: Readonly<StoryVocabulary> = {
  timesOfDay: ['DAY', 'NIGHT', 'DAWN', 'DUSK', 'CONTINUOUS', 'LATER'],
  transitions: ['CUT TO', 'DISSOLVE TO', 'SMASH CUT TO', 'MATCH CUT TO', 'TIME CUT', 'FADE OUT'],
  characterKinds: [
    { label: 'Human', avatarShape: 'Circle' },
    { label: 'Robot', avatarShape: 'Square' },
    { label: 'Animal', avatarShape: 'Teardrop' },
    { label: 'Creature', avatarShape: 'Squircle' },
    { label: 'Voice', avatarShape: 'Circle' },
  ],
};
