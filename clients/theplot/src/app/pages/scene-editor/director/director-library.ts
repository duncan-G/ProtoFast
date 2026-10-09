import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NAME_MAX } from '../../../stories/library-names';
import { Avatar } from '../avatar';
import { DirectorLibraryTab } from '../director-library-tab';
import { initials, parseLocationQuery } from '../format';
import { SceneEditorStore } from '../scene-editor-store';
import { LibraryCastCard } from './library-cast-card';
import { LibraryEntryCard } from './library-entry-card';
import { LibraryNarratorCard } from './library-narrator-card';

const DEFAULTS = 'Scenes and panels can override them in the inspector.';

const TABS: { tab: DirectorLibraryTab; label: string; hint: string; placeholder: string }[] = [
  {
    tab: 'cast',
    label: 'Cast',
    hint: `How everyone looks and sounds across the story. ${DEFAULTS}`,
    placeholder: 'New character…',
  },
  {
    tab: 'places',
    label: 'Places',
    hint: `What every location looks like. ${DEFAULTS}`,
    placeholder: 'New place, e.g. EXT. Scrapyard',
  },
  {
    tab: 'props',
    label: 'Props',
    hint: `What every prop looks like. ${DEFAULTS}`,
    placeholder: 'New prop…',
  },
  {
    tab: 'narrators',
    label: 'Narrators',
    hint: 'The voices that read action and narration. The first is the default.',
    placeholder: '',
  },
];

/** The story's defaults: cast, places, props and narrators. A collapsed rail when closed. */
@Component({
  selector: 'app-director-library',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, LibraryCastCard, LibraryEntryCard, LibraryNarratorCard],
  host: {
    class: 'dr-library',
    '[class.is-open]': 'store.directorLibraryOpen()',
    'aria-label': 'Story library',
  },
  template: `
    @if (!store.directorLibraryOpen()) {
      <div class="dr-rail">
        <button
          type="button"
          class="dr-rail-toggle"
          title="Open story library"
          aria-label="Open story library"
          (click)="store.directorLibraryOpen.set(true)"
        >
          »
        </button>
        <span class="dr-rail-kicker">Cast</span>
        @for (character of store.characters(); track character.id) {
          <button
            type="button"
            class="dr-rail-avatar"
            [title]="character.name"
            (click)="openTo('cast', character.id)"
          >
            <app-avatar
              [shape]="store.kindOf(character).avatarShape"
              [hue]="character.hue"
              [initials]="initialsOf(character.name)"
              [size]="30"
            />
          </button>
        }
        <span class="dr-rail-rule"></span>
        <button type="button" class="dr-rail-count" (click)="openTo('places', null)">
          <span class="dr-rail-kicker">Places</span><span>{{ store.locations().length }}</span>
        </button>
        <button type="button" class="dr-rail-count" (click)="openTo('props', null)">
          <span class="dr-rail-kicker">Props</span><span>{{ store.props().length }}</span>
        </button>
      </div>
    } @else {
      <div class="se-library-head">
        <div class="dr-library-title">
          <span class="se-kicker">Story library</span>
          <button
            type="button"
            class="dr-rail-toggle"
            title="Collapse"
            aria-label="Collapse the story library"
            (click)="store.directorLibraryOpen.set(false)"
          >
            «
          </button>
        </div>
        <div class="se-tabs" role="tablist">
          @for (t of tabs; track t.tab) {
            <button
              type="button"
              class="se-tab"
              role="tab"
              [attr.aria-selected]="store.directorLibraryTab() === t.tab"
              [attr.aria-label]="t.label"
              [title]="t.label"
              (click)="selectTab(t.tab)"
            >
              <svg
                class="se-tab-icon"
                viewBox="0 0 20 20"
                fill="none"
                stroke="currentColor"
                stroke-width="1.5"
                stroke-linecap="round"
                stroke-linejoin="round"
                aria-hidden="true"
              >
                @switch (t.tab) {
                  @case ('cast') {
                    <circle cx="7.5" cy="6.5" r="2.75" />
                    <path d="M2.5 16.5c0-2.9 2.2-5 5-5s5 2.1 5 5" />
                    <path d="M13 4a2.75 2.75 0 0 1 0 5.2" />
                    <path d="M14.5 11.8c1.8.6 3 2.4 3 4.7" />
                  }
                  @case ('places') {
                    <path d="M10 17.5s5.5-5 5.5-9.5a5.5 5.5 0 0 0-11 0c0 4.5 5.5 9.5 5.5 9.5Z" />
                    <circle cx="10" cy="8" r="2" />
                  }
                  @case ('props') {
                    <path d="M10 2.5 17 6v8l-7 3.5L3 14V6l7-3.5Z" />
                    <path d="M3 6l7 3.5L17 6" />
                    <path d="M10 9.5v8" />
                  }
                  @case ('narrators') {
                    <rect x="7" y="2.5" width="6" height="9.5" rx="3" />
                    <path d="M4.5 9.5a5.5 5.5 0 0 0 11 0" />
                    <path d="M10 15v2.5" />
                    <path d="M7 17.5h6" />
                  }
                }
              </svg>
              <span class="se-tab-count">{{ counts()[t.tab] }}</span>
            </button>
          }
        </div>
        <p class="se-library-hint">{{ hint() }}</p>
      </div>

      <div class="dr-library-list" role="tabpanel">
        @switch (store.directorLibraryTab()) {
          @case ('cast') {
            @for (character of store.characters(); track character.id) {
              <app-library-cast-card [character]="character" />
            } @empty {
              <p class="dr-empty">No characters yet.</p>
            }
          }
          @case ('places') {
            @for (location of store.locations(); track location.id) {
              <app-library-entry-card [target]="{ kind: 'location', id: location.id }" />
            } @empty {
              <p class="dr-empty">No places yet.</p>
            }
          }
          @case ('props') {
            @for (prop of store.props(); track prop.id) {
              <app-library-entry-card [target]="{ kind: 'prop', id: prop.id }" />
            } @empty {
              <p class="dr-empty">No props yet.</p>
            }
          }
          @case ('narrators') {
            @for (narrator of store.narrators(); track narrator.id || $index; let i = $index) {
              <app-library-narrator-card [narrator]="narrator" [index]="i" />
            }
            <button type="button" class="dr-add-dashed" (click)="store.addNarrator()">
              + Add narrator
            </button>
            <span class="dr-hint"
              >The default narrates every action and narration line. Pick a different one on any
              line in the script.</span
            >
          }
        }
      </div>

      @if (placeholder(); as placeholder) {
        <form class="dr-library-add" (submit)="$event.preventDefault(); add()">
          <input
            class="dr-input"
            [placeholder]="placeholder"
            [attr.aria-label]="placeholder"
            [attr.maxlength]="nameMax"
            [value]="draft()"
            (input)="draft.set($any($event.target).value); problem.set('')"
          />
          <button type="submit" class="dr-btn is-dark" [disabled]="!draft().trim()">Add</button>
        </form>
        @if (problem()) {
          <span class="dr-error dr-library-problem">{{ problem() }}</span>
        }
      }
    }
  `,
})
export class DirectorLibrary {
  protected readonly store = inject(SceneEditorStore);

  protected readonly tabs = TABS;
  protected readonly nameMax = NAME_MAX;
  protected readonly draft = signal('');
  protected readonly problem = signal('');

  protected readonly hint = computed(
    () => TABS.find((t) => t.tab === this.store.directorLibraryTab())?.hint ?? '',
  );
  protected readonly counts = computed<Record<DirectorLibraryTab, number>>(() => ({
    cast: this.store.characters().length,
    places: this.store.locations().length,
    props: this.store.props().length,
    narrators: this.store.narrators().length,
  }));

  protected readonly placeholder = computed(
    () => TABS.find((t) => t.tab === this.store.directorLibraryTab())?.placeholder ?? '',
  );

  protected initialsOf(name: string): string {
    return initials(name);
  }

  protected selectTab(tab: DirectorLibraryTab): void {
    this.store.directorLibraryTab.set(tab);
    this.draft.set('');
    this.problem.set('');
  }

  protected openTo(tab: DirectorLibraryTab, entryId: string | null): void {
    this.store.directorLibraryOpen.set(true);
    this.store.directorLibraryTab.set(tab);
    this.store.directorLibraryEntry.set(entryId);
  }

  protected async add(): Promise<void> {
    const name = this.draft().trim();
    if (!name) {
      return;
    }
    let created: { id: string } | string;
    switch (this.store.directorLibraryTab()) {
      case 'cast':
        created = await this.store.addCharacter(name);
        break;
      case 'places': {
        const { setting, name: place } = parseLocationQuery(name);
        created = await this.store.addLocation(place, setting ?? 'Interior');
        break;
      }
      case 'props':
        created = await this.store.addProp(name);
        break;
      default:
        return;
    }
    if (typeof created === 'string') {
      this.problem.set(created);
      return;
    }
    this.draft.set('');
    this.store.directorLibraryEntry.set(created.id);
  }
}
