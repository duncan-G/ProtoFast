import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { extraOnScreenOf } from '../../../direction/inheritance';
import { emptyDirection } from '../../../direction/model/scene-direction';
import { Extra } from '../../../stories/model/extra';
import { plural } from '../format';
import { SceneEditorStore } from '../scene-editor-store';
import { LibraryPicture } from './library-picture';

/** Extras, one person or a group: what they look like in every scene, and whether this one has them. */
@Component({
  selector: 'app-library-extra-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LibraryPicture],
  host: { class: 'dr-card', '[class.is-open]': 'open()' },
  template: `
    @let e = extra();
    <div
      class="dr-card-head"
      role="button"
      tabindex="0"
      [attr.aria-expanded]="open()"
      (click)="toggle()"
      (keydown.enter)="toggle()"
    >
      <span class="dr-entry-mark"></span>
      <span class="dr-card-name">
        <span>{{ e.name }}</span>
        <span class="dr-card-sub">{{ e.description || 'Extras' }}</span>
      </span>
      <span class="dr-chev" aria-hidden="true">{{ open() ? '▴' : '▾' }}</span>
    </div>
    @if (open()) {
      <div class="dr-card-body">
        <app-library-picture [target]="{ kind: 'extra', id: e.id }" [name]="e.name" />
        <div class="dr-field">
          <span class="dr-kicker">Name</span>
          <input
            class="dr-input"
            maxlength="255"
            aria-label="Name"
            [value]="e.name"
            (change)="save({ name: $any($event.target).value })"
          />
        </div>
        <div class="dr-count">
          <span>Headcount</span>
          <input
            class="dr-input dr-input-mono"
            type="number"
            min="1"
            max="500"
            step="1"
            placeholder="?"
            aria-label="Headcount"
            [value]="e.headcount ?? ''"
            (change)="save({ headcount: headcount($any($event.target).value) })"
          />
          <span class="dr-hint">{{ countHint() }}</span>
        </div>
        <div class="dr-field">
          <div class="dr-field-head">
            <span class="dr-kicker">Description</span>
            <span class="flex-1"></span>
            <button
              type="button"
              class="dr-link"
              title="Have the model write the description and wardrobe from the scenes they are in"
              [class.is-working]="store.describing().has(e.id)"
              [disabled]="store.describing().has(e.id)"
              (click)="store.describe({ kind: 'extra', id: e.id })"
            >
              {{ store.describing().has(e.id) ? 'Writing…' : 'Suggest' }}
            </button>
          </div>
          <textarea
            class="dr-input"
            rows="3"
            maxlength="4000"
            placeholder="Who they are and how many, e.g. a dozen regulars in their sixties"
            [value]="e.description ?? ''"
            (change)="save({ description: $any($event.target).value.trim() || null })"
          ></textarea>
        </div>
        <div class="dr-field">
          <span class="dr-kicker">Wardrobe</span>
          <textarea
            class="dr-input"
            rows="2"
            maxlength="4000"
            placeholder="What they wear, e.g. flat caps, cardigans, work boots"
            [value]="e.wardrobe ?? ''"
            (change)="save({ wardrobe: $any($event.target).value.trim() || null })"
          ></textarea>
        </div>
        @if (problem()) {
          <span class="dr-error">{{ problem() }}</span>
        }
        <div class="dr-card-foot flex items-center gap-2">
          <span class="flex-1">{{ usage() }}</span>
          @if (inScene()) {
            <button
              type="button"
              class="dr-link is-quiet"
              (click)="store.removeExtraFromScene(e.id)"
            >
              Take out of scene
            </button>
          } @else {
            <button type="button" class="dr-link is-quiet" (click)="store.addExtraToScene(e.id)">
              Add to scene
            </button>
          }
          <button type="button" class="dr-link is-quiet" (click)="confirming.set(true)">
            Delete
          </button>
        </div>
        @if (confirming()) {
          <div class="se-confirm rise" role="alert">
            <span>Delete “{{ e.name }}”? They leave every scene and frame they are in.</span>
            <div class="flex gap-1.5">
              <button type="button" class="se-btn se-btn-danger" (click)="store.removeExtra(e.id)">
                Delete
              </button>
              <button type="button" class="se-btn" (click)="confirming.set(false)">Keep</button>
            </div>
          </div>
        }
      </div>
    }
  `,
})
export class LibraryExtraCard {
  protected readonly store = inject(SceneEditorStore);

  readonly extra = input.required<Extra>();

  protected readonly problem = signal('');
  protected readonly confirming = signal(false);

  protected readonly open = computed(() => this.store.directorLibraryEntry() === this.extra().id);
  protected readonly inScene = computed(() =>
    this.store.extrasInScene().some((e) => e.id === this.extra().id),
  );

  protected readonly usage = computed(() => {
    const scene = (this.store.place()?.index ?? 0) + 1;
    if (!this.inScene()) {
      return `Not in Scene ${scene}`;
    }
    const direction = this.store.direction() ?? emptyDirection('');
    const spans = this.store.frameSpans();
    const shown = spans.filter(
      (s) => extraOnScreenOf(direction, s.frame, this.extra().id).value,
    ).length;
    return `In Scene ${scene} · on screen in ${shown} of ${plural(spans.length, 'frame')}`;
  });

  protected readonly countHint = computed(() => {
    const count = this.extra().headcount;
    return count === null
      ? 'as the description says'
      : count === 1
        ? 'one person, alone'
        : 'people, drawn as one group';
  });

  protected headcount(value: string): number | null {
    const count = Math.round(+value);
    return count >= 1 ? count : null;
  }

  protected toggle(): void {
    this.store.directorLibraryEntry.set(this.open() ? null : this.extra().id);
  }

  protected async save(patch: Partial<Extra>): Promise<void> {
    this.problem.set((await this.store.saveExtra({ ...this.extra(), ...patch })) ?? '');
  }
}
