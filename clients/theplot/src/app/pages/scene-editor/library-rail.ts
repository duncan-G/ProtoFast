import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Avatar } from './avatar';
import { initials } from './format';
import { LibraryTab } from './library-tab';
import { SceneEditorStore } from './scene-editor-store';

/** Story mode's library folded to the director library's rail. */
@Component({
  selector: 'app-library-rail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar],
  host: { class: 'se-library is-collapsed', 'aria-label': 'Story library' },
  template: `
    <div class="dr-rail">
      <button
        type="button"
        class="dr-rail-toggle"
        title="Open story library"
        aria-label="Open story library"
        (click)="store.libraryCollapsed.set(false)"
      >
        »
      </button>
      <span class="dr-rail-kicker">Cast</span>
      <div class="dr-rail-cast">
        @for (character of store.characters(); track character.id) {
          <button
            type="button"
            class="dr-rail-avatar"
            [title]="character.name"
            (click)="openTo('characters', character.id)"
          >
            <app-avatar
              [shape]="store.kindOf(character).avatarShape"
              [hue]="character.hue"
              [initials]="initialsOf(character.name)"
              [size]="30"
            />
          </button>
        }
      </div>
      <span class="dr-rail-rule"></span>
      <button type="button" class="dr-rail-count" (click)="openTo('locations', null)">
        <span class="dr-rail-kicker">Places</span><span>{{ store.locations().length }}</span>
      </button>
      <button type="button" class="dr-rail-count" (click)="openTo('props', null)">
        <span class="dr-rail-kicker">Props</span><span>{{ store.props().length }}</span>
      </button>
    </div>
  `,
})
export class LibraryRail {
  protected readonly store = inject(SceneEditorStore);

  protected initialsOf(name: string): string {
    return initials(name);
  }

  protected openTo(tab: LibraryTab, entryId: string | null): void {
    this.store.libraryTab.set(tab);
    this.store.libraryEntry.set(entryId);
    this.store.libraryCollapsed.set(false);
  }
}
