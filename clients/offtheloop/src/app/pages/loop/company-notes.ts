import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NoteBody } from '../../editors/note-body';
import { TagPicker } from '../../editors/tag-picker';
import { messageOf } from '../../loop/errors';
import { LoopEditors } from '../../loop/loop-editors';
import { LoopStore } from '../../loop/loop-store';
import { Company } from '../../loop/model/company';
import { Note } from '../../loop/model/note';
import { tagChoices, tagKey } from '../../loop/note-tags';
import { Icon } from '../../shared/icon';
import { riseIn } from '../../shared/rise-in';

/** A company's own notes: each opens under its row to be written in, and is renamed in place. */
@Component({
  selector: 'app-company-notes',
  imports: [FormsModule, Icon, NoteBody, RouterLink, TagPicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dim-scope flex min-w-0 flex-col', role: 'group', 'aria-label': 'Notes' },
  template: `
    <h3 class="caption mb-1.5">
      notes
      @if (notes().length) {
        <span class="count">{{ notes().length }}</span>
      }
    </h3>
    <form class="item item-add mb-1" (ngSubmit)="add()" (keydown.escape)="fresh = ''">
      <app-icon name="plus" [size]="11" [weight]="1.8" />
      <label class="sr-only" for="company-note-add">Add a note on {{ company().name }}</label>
      <input
        id="company-note-add"
        class="item-input"
        name="fresh"
        autocomplete="off"
        placeholder="add one, e.g. Onsite — the four rounds"
        [(ngModel)]="fresh"
      />
    </form>
    @if (error()) {
      <p class="alert my-2" role="alert">{{ error() }}</p>
    }

    @for (note of notes(); track note.id) {
      @if (editing() === note.id) {
        <form
          class="edit-form edit-panel my-2"
          aria-labelledby="company-note-heading"
          (ngSubmit)="save()"
          (keydown)="onKeydown($event)"
        >
          <div class="edit-head">
            <h4 id="company-note-heading" class="edit-heading">editing · {{ note.title }}</h4>
          </div>
          <div class="edit-body">
            <div class="form-grid">
              <div class="field">
                <div class="field-head"><label for="company-note-title">title</label></div>
                <input
                  id="company-note-title"
                  class="input font-medium"
                  name="title"
                  required
                  autocomplete="off"
                  [(ngModel)]="draft.title"
                />
              </div>
              <div class="field">
                <div class="field-head"><label for="company-note-tag">tag</label></div>
                <app-tag-picker
                  inputId="company-note-tag"
                  placeholder="pick or type one"
                  [tags]="tags()"
                  [(value)]="draft.tag"
                />
              </div>
            </div>
            @if (editError()) {
              <p class="alert" role="alert">{{ editError() }}</p>
            }
          </div>
          <div class="edit-foot">
            @if (confirmingDelete()) {
              <span class="mr-auto flex items-center gap-2.5">
                <span class="text-[13px] text-[var(--color-muted)]">Delete for good?</span>
                <button
                  type="button"
                  class="btn btn-sm btn-danger"
                  [disabled]="busy()"
                  (click)="remove()"
                >
                  Delete
                </button>
                <button type="button" class="btn-link" (click)="confirmingDelete.set(false)">
                  keep
                </button>
              </span>
            } @else {
              <button
                type="button"
                class="btn btn-sm btn-danger mr-auto"
                [disabled]="busy()"
                (click)="confirmingDelete.set(true)"
              >
                Delete
              </button>
            }
            <button type="button" class="btn btn-sm" (click)="stop()">Cancel</button>
            <button
              type="submit"
              class="btn btn-sm btn-primary"
              [disabled]="!draft.title.trim() || busy()"
            >
              Save
            </button>
          </div>
        </form>
      } @else {
        <a
          class="row-pick"
          routerLink="/app/loop"
          [queryParams]="{ note: note.id === open()?.id ? null : note.id }"
          queryParamsHandling="merge"
          [replaceUrl]="true"
          [attr.aria-current]="note.id === open()?.id"
        >
          <span class="flex-1 truncate font-medium">{{ note.title }}</span>
          @if (tagKey(note.tag); as tag) {
            <span class="meta">{{ tag }}</span>
          }
        </a>
      }
      @if (note.id === open()?.id) {
        <div
          #sheet
          class="flex flex-col gap-3 px-3 pt-3 pb-5"
          [class.opacity-0]="revealed() !== note.id"
        >
          <app-note-body [note]="note" (opened)="reveal($event)" />
          @if (editing() !== note.id) {
            <button type="button" class="btn-link self-start" (click)="edit(note)">
              title, tag
            </button>
          }
        </div>
      }
    }
  `,
})
export class CompanyNotes {
  private readonly store = inject(LoopStore);
  private readonly editors = inject(LoopEditors);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly sheet = viewChild<ElementRef<HTMLElement>>('sheet');
  private readonly params = toSignal(this.route.queryParamMap, { requireSync: true });

  readonly company = input.required<Company>();

  protected readonly tagKey = tagKey;
  protected readonly editing = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly editError = signal('');
  protected readonly confirmingDelete = signal(false);
  /** The open note once its editor is drawn; it fades in then. */
  protected readonly revealed = signal<string | null>(null);
  protected fresh = '';
  protected draft = { title: '', tag: '' };

  protected readonly notes = computed(() =>
    this.store.notes().filter((note) => note.companyId === this.company().id),
  );

  protected readonly open = computed(() => {
    const id = this.params().get('note');
    return this.notes().find((note) => note.id === id) ?? null;
  });

  protected readonly tags = computed(() => tagChoices('company', this.store.notes()));

  protected reveal(id: string): void {
    this.revealed.set(id);
    const sheet = this.sheet()?.nativeElement;
    if (sheet) {
      riseIn(sheet);
    }
  }

  /** A new note opens under its row with the cursor in it. */
  protected async add(): Promise<void> {
    const title = this.fresh.trim();
    if (!title || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      const saved = await this.store.saveNote({
        id: '',
        kind: 'company',
        companyId: this.company().id,
        title,
        tag: '',
        body: '',
        badges: [],
        fits: [],
      });
      this.fresh = '';
      this.editors.created.set(saved.id);
      this.show(saved.id);
    } catch (err) {
      this.error.set(messageOf(err, 'The note could not be added.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected edit(note: Note): void {
    this.draft = { title: note.title, tag: note.tag };
    this.editError.set('');
    this.confirmingDelete.set(false);
    this.editing.set(note.id);
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLElement>('#company-note-title')?.focus(),
      { injector: this.injector },
    );
  }

  protected stop(): void {
    this.editing.set(null);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
      event.preventDefault();
      void this.save();
    } else if (event.key === 'Escape') {
      event.preventDefault();
      this.stop();
    }
  }

  protected async save(): Promise<void> {
    // The body saves itself as it's written, so the store has its latest words.
    const note = this.store.notes().find((n) => n.id === this.editing());
    if (!note || !this.draft.title.trim() || this.busy()) {
      return;
    }
    await this.run(
      () => this.store.saveNote({ ...note, title: this.draft.title, tag: this.draft.tag }),
      'The note could not be saved.',
    );
  }

  protected async remove(): Promise<void> {
    const id = this.editing();
    if (!id || this.busy()) {
      return;
    }
    await this.run(async () => {
      await this.store.deleteNote(id);
      this.show(null);
    }, 'The note could not be deleted.');
  }

  private async run(write: () => Promise<unknown>, failure: string): Promise<void> {
    this.busy.set(true);
    this.editError.set('');
    try {
      await write();
      this.editing.set(null);
    } catch (err) {
      this.editError.set(messageOf(err, failure));
    } finally {
      this.busy.set(false);
    }
  }

  private show(note: string | null): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { note },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
}
