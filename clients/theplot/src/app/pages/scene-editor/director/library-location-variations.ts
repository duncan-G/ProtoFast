import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Location } from '../../../stories/model/location';
import { LocationVariation } from '../../../stories/model/location-variation';
import { SceneEditorStore } from '../scene-editor-store';
import { LibraryPicture } from './library-picture';

/**
 * A location's variations: the parts of the place the camera moves to. Each is pictured from the
 * location's own picture, and frames there take their background from it.
 */
@Component({
  selector: 'app-library-location-variations',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LibraryPicture],
  host: { class: 'dr-field' },
  template: `
    <div class="dr-field-head">
      <span class="dr-kicker">Variations</span>
      <span class="dr-count">{{ location().variations.length }}</span>
      <span class="flex-1"></span>
      <button type="button" class="dr-link" (click)="add()">+ Add</button>
    </div>
    <div class="dr-wardrobes">
      @for (v of rows(); track v.variation.id || v.variation.name) {
        <div class="dr-wardrobe" [class.is-open]="editing() === v.variation.name">
          <div
            class="dr-wardrobe-head"
            role="button"
            tabindex="0"
            (click)="toggle(v.variation.name)"
            (keydown.enter)="toggle(v.variation.name)"
          >
            <span class="dr-wardrobe-name">
              <span>{{ v.variation.name }}</span>
              @if (editing() !== v.variation.name) {
                <span class="dr-card-sub">{{
                  v.variation.description || 'No description yet'
                }}</span>
              }
            </span>
            @if (v.frames.length > 0) {
              <span class="dr-use" [title]="v.usage"
                >SC{{ sceneNumber() }} · {{ v.frames.length }}F</span
              >
            }
            <span class="dr-chev" aria-hidden="true">{{
              editing() === v.variation.name ? '▴' : '▾'
            }}</span>
          </div>
          @if (editing() === v.variation.name) {
            <div class="dr-wardrobe-body">
              <input
                class="dr-input"
                aria-label="Variation name"
                maxlength="255"
                [value]="v.variation.name"
                (change)="rename(v.variation, $any($event.target).value)"
              />
              <textarea
                class="dr-input"
                rows="3"
                maxlength="1000"
                placeholder="Where the camera stands and what it sees, e.g. the far end of the corridor, facing the lift doors"
                [value]="v.variation.description"
                (change)="update(v.variation, { description: $any($event.target).value.trim() })"
              ></textarea>
              @if (v.variation.id) {
                <app-library-picture
                  [target]="{ kind: 'locationVariation', id: v.variation.id }"
                  [name]="location().name + ' · ' + v.variation.name"
                />
              }
              <span class="dr-hint">{{ v.usage }}</span>
              <div class="dr-row-actions">
                <span class="flex-1"></span>
                <button type="button" class="dr-danger" (click)="remove(v.variation)">
                  Delete
                </button>
              </div>
            </div>
          }
        </div>
      } @empty {
        <div class="dr-wardrobe-empty">
          No variations yet. Add one for each part of the place the camera moves to.
        </div>
      }
    </div>
    @if (problem()) {
      <span class="dr-error">{{ problem() }}</span>
    }
  `,
})
export class LibraryLocationVariations {
  protected readonly store = inject(SceneEditorStore);

  readonly location = input.required<Location>();

  protected readonly editing = signal<string | null>(null);
  protected readonly problem = signal('');

  protected readonly sceneNumber = computed(() => (this.store.place()?.index ?? 0) + 1);

  protected readonly rows = computed(() => {
    const spans = this.store.frameSpans();
    return this.location().variations.map((variation) => {
      const frames = spans.flatMap((s, i) =>
        variation.id && s.frame.locationVariationId === variation.id ? [i + 1] : [],
      );
      const usage =
        frames.length === 0
          ? `No frame of Scene ${this.sceneNumber()} is here`
          : `${frames.length === 1 ? 'Frame' : 'Frames'} ${frames.join(', ')} of Scene ${this.sceneNumber()}`;
      return { variation, frames, usage };
    });
  });

  protected toggle(name: string): void {
    this.editing.set(this.editing() === name ? null : name);
  }

  protected add(): void {
    const names = new Set(this.location().variations.map((v) => v.name.toLowerCase()));
    let n = 1;
    while (names.has(`new part ${n}`)) {
      n++;
    }
    const name = `New part ${n}`;
    this.editing.set(name);
    void this.save([...this.location().variations, { id: '', name, description: '' }]);
  }

  protected update(variation: LocationVariation, patch: Partial<LocationVariation>): Promise<void> {
    return this.save(
      this.location().variations.map((v) => (v === variation ? { ...v, ...patch } : v)),
    );
  }

  protected rename(variation: LocationVariation, name: string): void {
    const next = name.trim();
    if (next && next !== variation.name) {
      this.editing.set(next);
      void this.update(variation, { name: next });
    }
  }

  protected remove(variation: LocationVariation): void {
    this.editing.set(null);
    void this.save(this.location().variations.filter((v) => v !== variation));
  }

  private async save(variations: LocationVariation[]): Promise<void> {
    this.problem.set((await this.store.saveLocationVariations(this.location(), variations)) ?? '');
  }
}
