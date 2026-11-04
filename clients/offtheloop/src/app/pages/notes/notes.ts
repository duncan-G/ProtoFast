import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NoteBody } from '../../editors/note-body';
import { LoopEditors } from '../../loop/loop-editors';
import { LoopStore } from '../../loop/loop-store';
import { Note } from '../../loop/model/note';
import { NOTE_KINDS, NoteKind } from '../../loop/model/note-kind';
import { NOTE_SORTS, NoteSort, search, sortNotes } from '../../loop/note-search';
import { tagKey, tagsOf } from '../../loop/note-tags';
import { Icon } from '../../shared/icon';
import { riseIn } from '../../shared/rise-in';
import { StoryImportSheet } from './story-import-sheet';

type KindFilter = NoteKind | 'all';

const NO_KIND: Partial<Record<NoteKind, string>> = {
  pattern: 'No patterns yet',
  design: 'No design notes yet',
};

const FILTERS: readonly { kind: KindFilter; label: string }[] = [
  { kind: 'all', label: 'all' },
  ...NOTE_KINDS.map(({ kind, plural }) => ({ kind, label: plural })),
];

/**
 * Every note in a list that scrolls on its own, the one picked open beside it. With a kind, only
 * notes of that kind, as the resume page shows its stories; what's projected heads the list.
 */
@Component({
  selector: 'app-notes',
  imports: [Icon, NoteBody, RouterLink, StoryImportSheet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="notes mx-auto max-w-[1120px]">
      <section
        aria-label="Notes"
        class="notes-list"
        [class.wide-only]="picked()"
        (keydown.arrowdown)="step(1, $event)"
        (keydown.arrowup)="step(-1, $event)"
      >
        <div class="flex flex-col gap-2 pb-3">
          <ng-content />
          <div class="flex items-center gap-3">
            <div
              class="flex min-w-0 flex-1 items-center gap-2.5 border-b border-[var(--color-edge)] focus-within:border-[var(--color-accent)]"
            >
              <span class="text-[var(--color-dim)]"><app-icon name="search" [size]="16" /></span>
              <label for="note-query" class="sr-only">{{
                only() === 'story' ? 'Search your stories' : 'Search your notes'
              }}</label>
              <input
                id="note-query"
                type="search"
                class="h-11 min-w-0 flex-1 border-0 bg-transparent text-[17px] tracking-[-0.01em] text-[var(--color-text)] outline-none placeholder:text-[var(--color-faint)]"
                [placeholder]="only() === 'story' ? 'Search stories' : 'Search notes'"
                autocomplete="off"
                [value]="query()"
                (input)="setQuery($any($event.target).value)"
              />
              <span class="mono text-[12px] text-[var(--color-dim)]">{{ results().length }}</span>
            </div>
            @if (only() === 'story') {
              <button
                type="button"
                class="btn btn-icon"
                aria-label="Import stories"
                title="Import stories from your git history"
                (click)="importing.set(true)"
              >
                <app-icon name="upload" [size]="16" />
              </button>
            }
            <button type="button" class="btn btn-primary wide-only pl-3.5" (click)="add()">
              <app-icon name="plus" [size]="14" [weight]="2" />{{
                only() === 'story' ? 'Story' : 'Note'
              }}
            </button>
          </div>
          @if (!only()) {
            <div
              role="group"
              aria-label="Filter by kind"
              class="flex flex-wrap gap-x-[18px] gap-y-1"
            >
              @for (filter of filters; track filter.kind) {
                <button
                  type="button"
                  class="chip"
                  [attr.aria-pressed]="kind() === filter.kind"
                  (click)="setKind(filter.kind)"
                >
                  {{ filter.label }}
                </button>
              }
            </div>
          }
          @if (pool().length) {
            <div class="flex items-center justify-between gap-4">
              <label class="note-pick min-w-0">
                <span>tag</span>
                <select
                  class="input"
                  [class.is-set]="tag()"
                  (change)="setTag($any($event.target).value)"
                >
                  <option value="" [selected]="!tag()">all</option>
                  @for (t of tags(); track t) {
                    <option [value]="t" [selected]="t === tag()">{{ t }}</option>
                  }
                </select>
              </label>
              <label class="note-pick">
                <span>sort</span>
                <select class="input" (change)="setSort($any($event.target).value)">
                  @for (s of sorts; track s) {
                    <option [value]="s" [selected]="s === sort()">{{ s }}</option>
                  }
                </select>
              </label>
            </div>
          }
        </div>

        <div #list class="notes-scroll">
          @for (group of groups(); track group.tag) {
            @if (group.tag !== null) {
              <h2 class="note-group">{{ group.tag || 'no tag' }}</h2>
            }
            @for (note of group.notes; track note.id) {
              <a
                class="note-row"
                [routerLink]="[]"
                [queryParams]="{ id: note.id }"
                queryParamsHandling="merge"
                [attr.data-note]="note.id"
                [attr.aria-current]="note.id === selected()?.id"
              >
                <span class="flex items-baseline gap-3">
                  <span class="truncate flex-1 font-medium">{{ note.title }}</span>
                  @if (kind() === 'all' && !only()) {
                    <span class="meta text-[11.5px]">{{ note.kind }}</span>
                  } @else if (only() && note.tag) {
                    <span class="meta text-[11.5px]">{{ tagKey(note.tag) }}</span>
                  }
                </span>
              </a>
            }
          }
          @if (!store.loaded()) {
            <p class="m-0 py-3.5 text-[var(--color-dim)]">Loading…</p>
          }
        </div>
      </section>

      @if (selected(); as note) {
        <article #page class="notes-page" [class.wide-only]="!picked()">
          <div
            #sheet
            class="flex max-w-[760px] flex-col gap-[26px]"
            [class.opacity-0]="revealed() !== note.id"
          >
            <a
              [routerLink]="[]"
              [queryParams]="{ id: null }"
              queryParamsHandling="merge"
              class="phone-only -mb-3 inline-flex min-h-[40px] items-center gap-1.5 self-start text-[14px] text-[var(--color-muted)] no-underline"
              ><app-icon name="back" [size]="14" />{{ only() === 'story' ? 'Stories' : 'Notes' }}</a
            >
            <div class="flex items-start justify-between gap-4">
              <div class="min-w-0">
                <p class="label m-0 text-[12px]">
                  {{ note.kind }}
                  @if (tagKey(note.tag); as noteTag) {
                    ·
                    <a
                      class="note-tag"
                      [routerLink]="[]"
                      [queryParams]="{
                        tag: noteTag,
                        id: note.id,
                        kind: kind() === note.kind ? kind() : null,
                      }"
                      queryParamsHandling="merge"
                      [attr.aria-label]="'Notes tagged ' + noteTag"
                      >{{ noteTag }}</a
                    >
                  }
                </p>
                <h1
                  class="mt-1.5 mb-0 text-[clamp(28px,3vw,38px)] leading-[1.08] font-semibold tracking-[-0.04em]"
                >
                  {{ note.title }}
                </h1>
              </div>
              <button
                type="button"
                class="btn btn-icon"
                aria-label="Note details"
                title="Kind, tag, badges and fits"
                (click)="editors.editNote(note)"
              >
                <app-icon name="edit" [size]="15" />
              </button>
            </div>

            <app-note-body [note]="note" (opened)="reveal($event)" />

            @if (note.badges.length) {
              <p class="mono m-0 text-[12px] text-[var(--color-dim)]">
                {{ note.badges.join(' · ') }}
              </p>
            }

            @if (note.fits.length) {
              <div
                class="flex flex-wrap gap-x-[18px] gap-y-1.5 border-t border-[var(--color-line)] pt-[18px]"
              >
                @for (fit of note.fits; track $index) {
                  <span class="text-[14px] text-[var(--color-muted)]">{{ fit }}</span>
                }
              </div>
            }
          </div>
        </article>
      } @else if (store.loaded()) {
        <article class="notes-page">
          <div class="flex max-w-[520px] flex-col items-start gap-[22px] max-[719px]:pt-6">
            <h1
              class="m-0 text-[clamp(28px,3vw,38px)] leading-[1.08] font-semibold tracking-[-0.04em]"
            >
              {{ emptyTitle() }}
            </h1>
            @if (!pool().length) {
              <p class="m-0 text-[17px] text-[var(--color-muted)]">
                @if (only() === 'story') {
                  Stories from your real work, for behavioural questions. The AI also draws on them
                  when it tailors a resume.
                } @else {
                  Note a coding pattern or a system design once, in your words.
                }
              </p>
            }
            <div class="flex flex-wrap gap-3">
              <button type="button" class="btn pl-3.5" (click)="jot()">
                <app-icon name="plus" [size]="14" [weight]="2" />{{
                  query() ? 'Jot down “' + query().trim() + '”' : 'Write one'
                }}
              </button>
              @if (!query() && !tag() && only() === 'story') {
                <button type="button" class="btn pl-3.5" (click)="importing.set(true)">
                  <app-icon name="upload" [size]="15" />Import stories from git
                </button>
              }
            </div>
          </div>
        </article>
      }
    </div>

    @if (importing()) {
      <app-story-import (closed)="importing.set(false)" />
    }
  `,
})
export class Notes {
  protected readonly store = inject(LoopStore);
  protected readonly editors = inject(LoopEditors);

  /** The one kind listed; the notes page lists its own kinds. */
  readonly only = input<NoteKind | null>(null);

  protected readonly pool = computed(() => {
    const only = this.only();
    return only
      ? this.store.notes().filter((n) => n.kind === only && !n.companyId)
      : this.store.libraryNotes();
  });
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly list = viewChild.required<ElementRef<HTMLElement>>('list');
  private readonly page = viewChild<ElementRef<HTMLElement>>('page');
  private readonly sheet = viewChild<ElementRef<HTMLElement>>('sheet');
  private readonly params = toSignal(this.route.queryParamMap, { requireSync: true });

  /** Kept after the back link clears the pick, so a phone's list comes back to it. */
  private lastPicked: string | null = null;
  private shown: string | null = null;
  /** The note whose page is drawn; another stays hidden until its editor is, then fades in. */
  protected readonly revealed = signal<string | null>(null);
  protected readonly importing = signal(false);

  protected readonly filters = FILTERS;
  protected readonly sorts = NOTE_SORTS;
  protected readonly tagKey = tagKey;
  protected readonly query = computed(() => this.params().get('q') ?? '');
  protected readonly kind = computed(
    () => this.only() ?? (this.params().get('kind') as KindFilter | null) ?? 'all',
  );
  protected readonly tag = computed(() => tagKey(this.params().get('tag') ?? ''));
  protected readonly sort = computed(() => {
    const sort = this.params().get('sort') as NoteSort | null;
    return sort && NOTE_SORTS.includes(sort) ? sort : 'recent';
  });
  /** A note opened from the list or a link; on a phone it takes the screen. */
  protected readonly picked = computed(() => this.params().get('id'));

  /** The tags of the kind shown, and the one filtered on even when no note carries it now. */
  protected readonly tags = computed(() => {
    const kind = this.kind();
    const tags = tagsOf(this.pool().filter((n) => kind === 'all' || n.kind === kind));
    const tag = this.tag();
    return !tag || tags.includes(tag) ? tags : [tag, ...tags];
  });

  protected readonly results = computed(() =>
    sortNotes(
      search(this.pool(), {
        query: this.query(),
        kind: this.kind(),
        tag: this.tag(),
      }),
      this.sort(),
    ),
  );

  /** One untitled group, or a group per tag when sorted by tag. */
  protected readonly groups = computed(() => {
    const notes = this.results();
    if (this.sort() !== 'tag' || this.tag()) {
      return [{ tag: null, notes }];
    }
    const groups: { tag: string; notes: Note[] }[] = [];
    for (const note of notes) {
      const tag = tagKey(note.tag);
      const last = groups.at(-1);
      if (last?.tag === tag) {
        last.notes.push(note);
      } else {
        groups.push({ tag, notes: [note] });
      }
    }
    return groups;
  });

  protected readonly emptyTitle = computed(() => {
    const kind = this.kind();
    if (!this.pool().length) {
      return this.only() === 'story' ? 'No stories yet' : 'No notes yet';
    }
    return (kind !== 'all' && !this.query() && !this.tag() && NO_KIND[kind]) || 'No match';
  });

  protected readonly selected = computed(() => {
    const id = this.picked();
    return (id ? this.pool().find((n) => n.id === id) : undefined) ?? this.results()[0] ?? null;
  });

  constructor() {
    afterRenderEffect(() => {
      const picked = this.picked();
      const shown = this.selected()?.id ?? null;
      this.results();
      if (picked) {
        this.lastPicked = picked;
      }

      const list = this.list().nativeElement;
      const target = picked ?? this.lastPicked;
      const row = target ? list.querySelector<HTMLElement>(`[data-note="${target}"]`) : null;
      if (row) {
        row.scrollIntoView({ block: picked ? 'nearest' : 'center' });
        if (document.activeElement?.matches('.note-row') && document.activeElement !== row) {
          row.focus({ preventScroll: true });
        }
      } else {
        list.scrollTop = 0;
      }

      if (shown !== this.shown) {
        this.shown = shown;
        this.page()?.nativeElement.scrollTo(0, 0);
        if (picked) {
          window.scrollTo(0, 0);
        }
      }
    });
  }

  protected reveal(id: string): void {
    this.revealed.set(id);
    const sheet = this.sheet()?.nativeElement;
    if (sheet) {
      riseIn(sheet);
    }
  }

  protected step(by: number, event: Event): void {
    const notes = this.results();
    const next = notes[notes.findIndex((n) => n.id === this.selected()?.id) + by];
    if (!next) {
      return;
    }
    event.preventDefault();
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { id: next.id },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  protected setQuery(q: string): void {
    this.lastPicked = null;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: q || null, id: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  protected setKind(kind: KindFilter): void {
    this.lastPicked = null;
    const tag = this.tag();
    const kept = this.store
      .libraryNotes()
      .some((n) => (kind === 'all' || n.kind === kind) && tagKey(n.tag) === tag);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { kind: kind === 'all' ? null : kind, tag: kept ? tag : null, id: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  protected setTag(tag: string): void {
    this.lastPicked = null;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tag: tag || null, id: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  protected setSort(sort: NoteSort): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { sort: sort === 'recent' ? null : sort },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  protected add(): void {
    this.editors.editNote(this.draft());
  }

  protected jot(): void {
    this.editors.editNote({ title: this.query().trim(), ...this.draft() });
  }

  /** A new note starts in the kind and tag being looked at. */
  private draft(): Partial<Note> {
    const kind = this.kind();
    const tag = this.tag();
    return { ...(kind === 'all' ? {} : { kind }), ...(tag ? { tag } : {}) };
  }
}
