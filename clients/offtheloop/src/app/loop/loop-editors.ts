import { Injectable, signal } from '@angular/core';
import { Note } from './model/note';

/** Which editor sheet is open. The shell draws it, so any page can open one. */
@Injectable({ providedIn: 'root' })
export class LoopEditors {
  readonly note = signal<Partial<Note> | null>(null);
  /** A note just made, whose page takes the cursor when it opens. */
  readonly created = signal<string | null>(null);

  editNote(note: Partial<Note> = {}): void {
    this.note.set(note);
  }
}
