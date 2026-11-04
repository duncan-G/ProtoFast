import { NoteKind } from './note-kind';

/** What the AI is asked to write into a note. */
export interface WriteRequest {
  noteId: string;
  instruction: string;
  /** The note's markdown, with ⟦cursor⟧, or ⟦selection⟧…⟦/selection⟧ around the selection. */
  document: string;
  selection: string;
  title: string;
  kind: NoteKind;
}
