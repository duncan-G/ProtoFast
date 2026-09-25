export type DeskMode = 'read' | 'write' | 'watch';

export const DESK_MODES: readonly DeskMode[] = ['read', 'write', 'watch'];

export interface ModeInfo {
  label: string;
  eyebrow: string;
  title: string;
  searchHint: string;
  tabs: readonly string[];
  /** The line on an empty list, per tab. Index 0 is "All". */
  empty: readonly [title: string, body: string][];
}

/**
 * The three modes in the top bar. Read and Watch have no
 * data behind them yet — publishing and adaptation are not built — so each says what will
 * land there rather than showing a sample of it.
 */
export const MODES: Record<DeskMode, ModeInfo> = {
  read: {
    label: 'Read',
    eyebrow: 'Read',
    title: 'Reading list',
    searchHint: 'Search stories, authors…',
    tabs: ['All', 'In progress', 'Following', 'Finished'],
    empty: [
      ['Nothing on the list yet.', 'Stories that writers publish on ThePlot will appear here to read as prose.'],
      ['Nothing in progress.', 'Open a story from the list and it keeps your place here.'],
      ['Not following anyone yet.', 'Follow a writer and their new chapters gather here.'],
      ['Nothing finished yet.', 'Stories you read to the end move here.'],
    ],
  },
  write: {
    label: 'Write',
    eyebrow: 'Write',
    title: 'Your desk',
    searchHint: 'Search your stories…',
    tabs: ['All', 'Drafts', 'Published', 'Adapting'],
    empty: [
      ['Your desk is clear.', 'Import a file and it lands here, kept exactly as you sent it.'],
      ['No drafts yet.', 'The chapter editor is on its way; imported files sit under All for now.'],
      ['Nothing published yet.', 'Publishing opens once the chapter editor lands.'],
      ['Nothing adapting yet.', 'Adaptation turns a published story into scenes. It isn’t built yet.'],
    ],
  },
  watch: {
    label: 'Watch',
    eyebrow: 'Watch',
    title: 'Screening room',
    searchHint: 'Search screenings…',
    tabs: ['All', 'Continue watching', 'New scenes', 'Watchlist'],
    empty: [
      ['The room is dark.', 'Adaptations play here once a story has been turned into scenes.'],
      ['Nothing to continue.', 'Start a screening and it keeps your place here.'],
      ['No new scenes.', 'Fresh scenes from stories you follow show up here.'],
      ['Your watchlist is empty.', 'Save a screening for later and it waits here.'],
    ],
  },
};

export function parseMode(value: string | null): DeskMode {
  return DESK_MODES.includes(value as DeskMode) ? (value as DeskMode) : 'write';
}
