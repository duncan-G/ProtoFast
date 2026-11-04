import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  OnInit,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { messageOf } from '../loop/errors';
import { LoopEditors } from '../loop/loop-editors';
import { LoopStore } from '../loop/loop-store';
import { Note } from '../loop/model/note';
import { NOTE_KINDS, NoteKind } from '../loop/model/note-kind';
import { tagChoices } from '../loop/note-tags';
import { Icon } from '../shared/icon';
import { lines, splitList } from './lists';
import { TagPicker } from './tag-picker';

const TITLES: Record<NoteKind, string> = {
  pattern: 'Monotonic stack',
  design: 'Idempotency keys',
  company: 'Onsite — the four rounds',
  story: 'Shipped the search migration two weeks late',
};

@Component({
  selector: 'app-note-editor',
  imports: [FormsModule, Icon, TagPicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog
      #sheet
      class="sheet"
      aria-labelledby="note-editor-title"
      (close)="closed.emit()"
      (click)="closeOnBackdrop($event)"
    >
      <form (ngSubmit)="save()">
        <div class="sheet-body">
          <div class="flex items-start justify-between gap-4">
            <h2 id="note-editor-title" class="m-0 text-[24px] font-semibold tracking-[-0.04em]">
              {{ editing() ? 'Note details' : 'Jot a note' }}
            </h2>
            <button
              type="button"
              class="btn btn-icon -mt-1 -mr-3 border-0"
              aria-label="Close"
              (click)="close()"
            >
              <app-icon name="close" />
            </button>
          </div>

          <div role="radiogroup" aria-label="Kind" class="flex flex-wrap gap-x-[18px] gap-y-1">
            @for (option of kinds; track option.kind) {
              <button
                type="button"
                role="radio"
                class="chip"
                [attr.aria-checked]="form.kind === option.kind"
                (click)="setKind(option.kind)"
              >
                {{ option.kind }}
              </button>
            }
          </div>

          <div class="grid-2">
            <label class="field">
              <span>title</span>
              <input
                class="input"
                name="title"
                required
                autocomplete="off"
                [placeholder]="placeholder()"
                [(ngModel)]="form.title"
              />
            </label>
            <div class="field">
              <span><label for="note-tag">tag</label></span>
              <app-tag-picker
                inputId="note-tag"
                placeholder="pick or type one"
                [tags]="tags()"
                [(value)]="form.tag"
              />
            </div>
          </div>
          <label class="field">
            <span>badges · comma between</span>
            <input
              class="input"
              name="badges"
              autocomplete="off"
              placeholder="O(n) time, O(n) space"
              [(ngModel)]="form.badges"
            />
          </label>
          <label class="field">
            <span>fits · one per line</span>
            <textarea
              class="input"
              name="fits"
              rows="3"
              placeholder="Daily Temperatures"
              [(ngModel)]="form.fits"
            ></textarea>
          </label>

          @if (error()) {
            <p class="alert" role="alert">{{ error() }}</p>
          }
        </div>

        <div class="sheet-foot">
          <button type="submit" class="btn btn-primary" [disabled]="!form.title.trim() || busy()">
            @if (busy()) {
              <span class="spinner" aria-hidden="true"></span>
            }
            {{ editing() ? 'Save' : 'Save note' }}
          </button>
          <button type="button" class="btn" (click)="close()">Cancel</button>
          @if (editing()) {
            <span class="flex-1"></span>
            @if (confirmingDelete()) {
              <span class="text-[13px] text-[var(--color-muted)]">Delete for good?</span>
              <button type="button" class="btn btn-danger" [disabled]="busy()" (click)="remove()">
                Delete
              </button>
              <button type="button" class="btn-link" (click)="confirmingDelete.set(false)">
                keep
              </button>
            } @else {
              <button
                type="button"
                class="btn btn-icon btn-danger border-0"
                aria-label="Delete note"
                (click)="confirmingDelete.set(true)"
              >
                <app-icon name="trash" [size]="16" />
              </button>
            }
          }
        </div>
      </form>
    </dialog>
  `,
})
export class NoteEditor implements OnInit {
  private readonly store = inject(LoopStore);
  private readonly editors = inject(LoopEditors);
  private readonly router = inject(Router);
  private readonly sheet = viewChild.required<ElementRef<HTMLDialogElement>>('sheet');

  readonly draft = input.required<Partial<Note>>();
  readonly closed = output<void>();

  protected readonly kinds = NOTE_KINDS;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly confirmingDelete = signal(false);
  protected readonly editing = computed(() => !!this.draft().id);
  protected readonly kind = signal<NoteKind>('pattern');
  protected readonly placeholder = computed(() => TITLES[this.kind()]);
  protected readonly tags = computed(() => tagChoices(this.kind(), this.store.notes()));

  protected form = {
    kind: 'pattern' as NoteKind,
    title: '',
    tag: '',
    badges: '',
    fits: '',
  };

  constructor() {
    afterNextRender(() => this.sheet().nativeElement.showModal());
  }

  ngOnInit(): void {
    const note = this.draft();
    this.form = {
      kind: note.kind ?? 'pattern',
      title: note.title ?? '',
      tag: note.tag ?? '',
      badges: (note.badges ?? []).join(', '),
      fits: lines(note.fits ?? []),
    };
    this.kind.set(this.form.kind);
  }

  protected setKind(kind: NoteKind): void {
    this.form.kind = kind;
    this.kind.set(kind);
  }

  protected close(): void {
    this.sheet().nativeElement.close();
  }

  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.sheet().nativeElement) {
      this.close();
    }
  }

  protected async save(): Promise<void> {
    if (!this.form.title.trim() || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      const id = this.draft().id ?? '';
      const saved = await this.store.saveNote({
        id,
        kind: this.form.kind,
        companyId: null,
        title: this.form.title,
        tag: this.form.tag,
        // The page saves itself as it is written, so the store has its latest words.
        body: this.store.notes().find((n) => n.id === id)?.body ?? this.draft().body ?? '',
        badges: splitList(this.form.badges, ','),
        fits: splitList(this.form.fits, '\n'),
      });
      this.close();
      if (!this.editing()) {
        this.editors.created.set(saved.id);
        void this.router.navigate(['/app/notes'], { queryParams: { id: saved.id } });
      }
    } catch (err) {
      this.error.set(messageOf(err, 'The note could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const id = this.draft().id;
    if (!id || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.store.deleteNote(id);
      this.close();
      void this.router.navigate(['/app/notes']);
    } catch (err) {
      this.error.set(messageOf(err, 'The note could not be deleted.'));
    } finally {
      this.busy.set(false);
    }
  }
}
