import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { heldIn, inViewOf } from '../../../direction/inheritance';
import { emptyDirection } from '../../../direction/model/scene-direction';
import { ReferenceTarget } from '../../../stories/model/reference-target';
import { plural, settingPrefix } from '../format';
import { SceneEditorStore } from '../scene-editor-store';
import { SheetEditor } from '../sheet-editor';
import { LibraryLocationVariations } from './library-location-variations';
import { LibraryPicture } from './library-picture';

/** A place or a prop: its description for every scene, and where this scene uses it. */
@Component({
  selector: 'app-library-entry-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LibraryLocationVariations, LibraryPicture, SheetEditor],
  host: { class: 'dr-card', '[class.is-open]': 'open()' },
  template: `
    @if (entry(); as e) {
      <div
        class="dr-card-head"
        role="button"
        tabindex="0"
        [attr.aria-expanded]="open()"
        (click)="toggle()"
        (keydown.enter)="toggle()"
      >
        <span class="dr-entry-mark hue-fill" [style.--hue]="e.hue"></span>
        <span class="dr-card-name">
          <span>{{ e.name }}</span>
          <span class="dr-card-sub">{{ e.sub }}</span>
        </span>
        <span class="dr-chev" aria-hidden="true">{{ open() ? '▴' : '▾' }}</span>
      </div>
      @if (open()) {
        <div class="dr-card-body">
          @if (target().kind === 'location') {
            <button
              type="button"
              class="dr-btn is-small self-start"
              title="Have the model write the whole sheet from the story: description, look and sound"
              [class.is-working]="store.describing().has(target().id)"
              [disabled]="store.describing().has(target().id)"
              (click)="store.describe(target())"
            >
              {{ store.describing().has(target().id) ? 'Suggesting…' : 'Suggest place' }}
            </button>
          }
          <app-library-picture [target]="target()" [name]="e.name" />
          <div class="dr-field">
            <div class="dr-field-head">
              <span class="dr-kicker">Description</span>
              @if (target().kind === 'prop') {
                <span class="flex-1"></span>
                <button
                  type="button"
                  class="dr-link"
                  title="Have the model write the sheet from the story"
                  [class.is-working]="store.describing().has(target().id)"
                  [disabled]="store.describing().has(target().id)"
                  (click)="store.describe(target())"
                >
                  {{ store.describing().has(target().id) ? 'Writing…' : 'Suggest' }}
                </button>
              }
            </div>
            <textarea
              class="dr-input"
              rows="3"
              maxlength="4000"
              [placeholder]="
                target().kind === 'location'
                  ? 'What it looks like'
                  : 'What it is and what it looks like'
              "
              [value]="e.description"
              (change)="save($any($event.target).value.trim() || null)"
            ></textarea>
          </div>
          @if (target().kind === 'location') {
            <button type="button" class="dr-link self-start" (click)="details.set(!details())">
              Look &amp; sound {{ details() ? '▴' : '▾' }}
            </button>
            @if (details()) {
              <app-sheet-editor [target]="target()" [details]="true" />
            }
            @if (store.locationById().get(target().id); as location) {
              <app-library-location-variations [location]="location" />
            }
          }
          @if (problem()) {
            <span class="dr-error">{{ problem() }}</span>
          }
          <div class="dr-card-foot">{{ usage() }}</div>
        </div>
      }
    }
  `,
})
export class LibraryEntryCard {
  protected readonly store = inject(SceneEditorStore);

  readonly target = input.required<ReferenceTarget>();

  protected readonly details = signal(false);
  protected readonly problem = signal('');

  protected readonly open = computed(() => this.store.directorLibraryEntry() === this.target().id);

  protected readonly entry = computed(() => {
    const { kind, id } = this.target();
    if (kind === 'location') {
      const location = this.store.locationById().get(id);
      return location
        ? {
            name: location.name,
            sub: settingPrefix(location.setting).replace('.', ''),
            hue: location.hue,
            description: location.description ?? '',
          }
        : null;
    }
    const prop = this.store.propById().get(id);
    return prop
      ? {
          name: prop.name,
          sub: 'Prop',
          hue: null,
          description: prop.description ?? '',
        }
      : null;
  });

  protected readonly usage = computed(() => {
    const { kind, id } = this.target();
    const scene = (this.store.place()?.index ?? 0) + 1;
    if (kind === 'location') {
      const headings = this.store
        .elements()
        .filter((e) => e.type === 'Heading' && e.locationId === id).length;
      if (headings) {
        return `Heads ${plural(headings, 'part')} of Scene ${scene}`;
      }
      return this.store.locationsInScene().some((l) => l.id === id)
        ? `In Scene ${scene}`
        : `Not in Scene ${scene}`;
    }
    if (!this.store.propsInScene().some((p) => p.id === id)) {
      return `Not in Scene ${scene}`;
    }
    const direction = this.store.direction() ?? emptyDirection('');
    const spans = this.store.frameSpans();
    const order = this.store.elementOrder();
    const shown = spans.filter(
      (s) => inViewOf(direction, s.frame, id, heldIn(direction, s, order)).value,
    ).length;
    return `In Scene ${scene} · in view in ${shown} of ${plural(spans.length, 'frame')}`;
  });

  protected toggle(): void {
    this.store.directorLibraryEntry.set(this.open() ? null : this.target().id);
  }

  protected async save(description: string | null): Promise<void> {
    const { kind, id } = this.target();
    if (kind === 'location') {
      const location = this.store.locationById().get(id);
      if (location) {
        this.problem.set((await this.store.saveLocationSheet({ ...location, description })) ?? '');
      }
    } else if (kind === 'prop') {
      const prop = this.store.propById().get(id);
      if (prop) {
        this.problem.set((await this.store.savePropSheet({ ...prop, description })) ?? '');
      }
    }
  }
}
