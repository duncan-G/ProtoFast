import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  viewChild,
} from '@angular/core';
import { PictureTarget } from '../../../direction/model/picture-target';
import { SceneEditorStore } from '../scene-editor-store';
import { PicturePreview } from './picture-preview';

const HINTS: Record<PictureTarget['kind'], string> = {
  character: 'A reference sheet drawn from the sheet; frames keep their face and build from it.',
  location: 'An empty establishing shot; frames here keep its layout from it.',
  locationVariation:
    'This part of the place, empty, drawn from the location’s picture; frames here take their background from it.',
  prop: 'The prop on its own; frames keep its look from it.',
  extra: 'Them together, drawn from the sheet; frames keep them the same from scene to scene.',
};

/** A library entry's picture, which every frame showing it is composed from. */
@Component({
  selector: 'app-library-picture',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PicturePreview],
  host: { class: 'dr-library-picture' },
  template: `
    @if (image(); as image) {
      <app-picture-preview
        [url]="image.url"
        [alt]="'Picture of ' + name()"
        [refinable]="true"
        [busy]="state() === 'generating'"
        [references]="store.refineReferences()"
        (refine)="store.refineLibraryImage(target(), $event)"
      />
    } @else {
      <div class="dr-picture">
        {{ state() === 'generating' ? 'Drawing…' : 'No image' }}
      </div>
    }
    <div class="dr-library-picture-side">
      <span class="dr-kicker">Picture</span>
      <span class="dr-hint" [class.is-warn]="state() === 'stale'">{{ status() }}</span>
      <span class="dr-picture-actions">
        @if (image()) {
          <button
            type="button"
            class="dr-link"
            [disabled]="state() === 'generating'"
            title="Say what to change in this picture; the rest stays"
            (click)="preview()?.open(true)"
          >
            Refine
          </button>
        }
        <button
          type="button"
          class="dr-link"
          [class.is-working]="state() === 'generating'"
          [disabled]="state() === 'generating'"
          (click)="store.generateLibraryImages([target()], state() === 'ready')"
        >
          {{ action() }}
        </button>
      </span>
    </div>
  `,
})
export class LibraryPicture {
  protected readonly store = inject(SceneEditorStore);

  readonly target = input.required<PictureTarget>();
  readonly name = input('');

  protected readonly preview = viewChild(PicturePreview);

  protected readonly image = computed(
    () => this.store.libraryImages().get(this.target().id)?.image,
  );
  protected readonly state = computed(() => this.store.libraryImageState(this.target().id));
  protected readonly status = computed(() =>
    this.state() === 'stale'
      ? this.target().kind === 'locationVariation'
        ? 'Out of date: the part or the location’s picture changed since.'
        : 'Out of date: the sheet changed since.'
      : HINTS[this.target().kind],
  );
  protected readonly action = computed(() => {
    switch (this.state()) {
      case 'generating':
        return 'Drawing…';
      case 'ready':
        return 'Regenerate';
      case 'stale':
        return 'Update';
      default:
        return 'Generate';
    }
  });
}
