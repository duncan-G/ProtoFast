/**
 * The landing page's copy, verbatim from the Claude Design project "ThePlot
 * app design system" (claude.ai/artifact/DNLkNWRTWf4Cp55boTN653). Kept out of
 * the template so the page reads as structure and the words read as words.
 */

export interface LandingStep {
  kicker: string;
  title: string;
  body: string;
}

/** How it works — write, adapt, premiere. */
export const LANDING_STEPS: LandingStep[] = [
  {
    kicker: 'I — Write',
    title: 'A quiet editor built for chapters.',
    body: 'Drafts, character notes and chapter order in one place. Publish when you’re ready — chapter by chapter or all at once.',
  },
  {
    kicker: 'II — Adapt',
    title: 'Your prose becomes scenes.',
    body: 'ThePlot storyboards each chapter, keeps characters consistent, and lets you approve, re-cut or lock any scene before it goes live.',
  },
  {
    kicker: 'III — Premiere',
    title: 'Readers choose how to experience it.',
    body: 'Read on the page, press play to watch, or switch between the two without losing their place.',
  },
];

export interface LandingStory {
  title: string;
  author: string;
  genre: string;
  read: string;
  watch: string;
}

/** The "Now showing" shelf. Artwork, not data — real stories come later. */
export const LANDING_STORIES: LandingStory[] = [
  { title: 'Salt & Static', author: 'Marisol Vega', genre: 'Sci-fi', read: '42 min', watch: '18 min' },
  { title: 'Nine Winters in Oda', author: 'Hana Mori', genre: 'Drama', read: '1 h 10', watch: '31 min' },
  {
    title: 'The Lighthouse Keeper’s Ledger',
    author: 'Tom Achebe-Reed',
    genre: 'Mystery',
    read: '55 min',
    watch: '24 min',
  },
  { title: 'Paper Moths', author: 'Ilse Brandt', genre: 'Fantasy', read: '38 min', watch: '15 min' },
];

/** The hero collage's manuscript beat and player caption. Static artwork. */
export const LANDING_MANUSCRIPT = {
  work: 'Salt & Static',
  chapter: 'Ch. 4',
  p1: 'The ferry had stopped running in March, but Ines still walked to the dock every evening, as if the water might change its mind.',
  highlighted:
    'Tonight the radio on the pier crackled awake, and a voice she had buried two winters ago said her name.',
  p3: 'She did not run. She sat down on the cold boards and listened.',
  still: 'generated scene still — pier at dusk',
  cue: '“Ines.”',
  meta: 'Scene 04 · 02:17',
};
