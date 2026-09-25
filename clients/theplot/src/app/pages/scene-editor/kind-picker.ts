import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { LABEL_MAX, sameName } from '../../stories/library-names';
import { AVATAR_SHAPES, AvatarShape } from '../../stories/model/avatar-shape';
import { Character } from '../../stories/model/character';
import { CharacterKind } from '../../stories/model/character-kind';
import { DEFAULT_VOCABULARY } from '../../stories/model/default-vocabulary';
import { Avatar } from './avatar';
import { SceneEditorStore } from './scene-editor-store';

@Component({
  selector: 'app-kind-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar],
  host: {
    class: 'se-pop se-kind-pop',
    role: 'dialog',
    'aria-label': 'Character kind',
    '(document:mousedown)': 'onDocumentDown($event)',
    '(keydown.escape)': 'close.emit()',
  },
  template: `
    <div class="se-pop-group">Kind</div>
    @for (kind of store.kinds(); track kind.label) {
      <button
        type="button"
        class="se-pop-item"
        [attr.aria-selected]="isCurrent(kind)"
        (click)="pick(kind)"
      >
        <app-avatar [shape]="kind.avatarShape" [hue]="character().hue" [size]="20" />
        <span class="se-pop-label">{{ kind.label }}</span>
        <span class="se-pop-meta">{{ isDefault(kind) ? '' : 'this story' }}</span>
        <span class="se-pop-check">{{ isCurrent(kind) ? '✓' : '' }}</span>
      </button>
    }
    <form class="se-kind-form" (submit)="$event.preventDefault(); add()">
      <span class="se-kicker">New kind</span>
      <input
        class="se-field"
        aria-label="New kind label"
        placeholder="e.g. Ghost, Hologram, Crowd"
        [attr.maxlength]="max"
        [class.is-invalid]="problem()"
        [value]="label()"
        (input)="label.set($any($event.target).value); problem.set('')"
      />
      <div class="se-shapes" role="group" aria-label="Avatar shape">
        @for (shape of shapes; track shape) {
          <button
            type="button"
            class="se-shape"
            [attr.aria-pressed]="shape === draftShape()"
            [attr.aria-label]="shape"
            [title]="shape"
            (click)="chosenShape.set(shape)"
          >
            <app-avatar [shape]="shape" [hue]="character().hue" [size]="20" />
          </button>
        }
      </div>
      @if (problem()) {
        <p class="se-error">{{ problem() }}</p>
      }
      <button type="submit" class="se-btn se-btn-primary self-start">Add kind</button>
    </form>
  `,
})
export class KindPicker {
  protected readonly store = inject(SceneEditorStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly character = input.required<Character>();
  /** Presses on it toggle the picker, so they don't count as outside. */
  readonly anchor = input.required<HTMLElement>();
  readonly close = output();

  protected readonly shapes = AVATAR_SHAPES;
  protected readonly max = LABEL_MAX;
  protected readonly label = signal('');
  protected readonly problem = signal('');
  protected readonly chosenShape = signal<AvatarShape | null>(null);
  /** Defaults to the first shape no kind uses yet. */
  protected readonly draftShape = computed(() => {
    const used = new Set(this.store.kinds().map((k) => k.avatarShape));
    return this.chosenShape() ?? AVATAR_SHAPES.find((s) => !used.has(s)) ?? 'Circle';
  });

  protected isCurrent(kind: CharacterKind): boolean {
    return sameName(kind.label, this.character().kind);
  }

  protected isDefault(kind: CharacterKind): boolean {
    return DEFAULT_VOCABULARY.characterKinds.some((k) => k.label === kind.label);
  }

  protected pick(kind: CharacterKind): void {
    void this.store.setCharacterKind(this.character().id, kind.label);
    this.close.emit();
  }

  protected async add(): Promise<void> {
    const kind = { label: this.label().trim(), avatarShape: this.draftShape() };
    const problem = await this.store.addCharacterKind(kind);
    if (problem) {
      this.problem.set(problem);
      return;
    }
    this.pick(kind);
  }

  protected onDocumentDown(event: MouseEvent): void {
    const target = event.target as Node;
    if (!this.host.nativeElement.contains(target) && !this.anchor().contains(target)) {
      this.close.emit();
    }
  }
}
