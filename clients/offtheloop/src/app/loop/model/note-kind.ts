export type NoteKind = 'pattern' | 'design' | 'company' | 'story';

/** The notes page's kinds; a company's notes are kept on its page, stories on the resume page. */
export const NOTE_KINDS: readonly { kind: NoteKind; plural: string }[] = [
  { kind: 'pattern', plural: 'patterns' },
  { kind: 'design', plural: 'design' },
];
