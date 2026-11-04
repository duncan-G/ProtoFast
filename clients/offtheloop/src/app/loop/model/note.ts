import { NoteKind } from './note-kind';

export interface Note {
  id: string;
  kind: NoteKind;
  /** Set exactly when the kind is company. */
  companyId: string | null;
  title: string;
  tag: string;
  /** Markdown, with $math$, $$math$$ and ```mermaid diagrams. */
  body: string;
  badges: string[];
  fits: string[];
}
