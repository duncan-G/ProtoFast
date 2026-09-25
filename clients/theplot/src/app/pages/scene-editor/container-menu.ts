import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { NAME_MAX } from '../../stories/library-names';
import { Container } from '../../stories/model/container';
import { plural } from './format';
import { SceneEditorStore } from './scene-editor-store';

@Component({
  selector: 'app-container-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'se-crumb-menu',
    '(document:mousedown)': 'onDocumentDown($event)',
    '(keydown.escape)': 'open.set(false)',
  },
  template: `
    <button
      type="button"
      class="se-crumb-btn"
      aria-haspopup="menu"
      [attr.aria-expanded]="open()"
      (click)="toggle()"
    >
      {{ store.place()?.container?.label ?? '…' }}
      <span class="text-[10px] text-[var(--se-muted)]">▾</span>
    </button>
    @if (open()) {
      <div class="se-pop se-menu" role="menu">
        <div class="se-pop-group">Containers</div>
        @for (container of containers(); track container.id) {
          @if (renaming() === container.id) {
            <form class="se-menu-form" (submit)="$event.preventDefault(); rename(container)">
              <input
                #field
                class="se-field"
                aria-label="Container label"
                [attr.maxlength]="max"
                [value]="container.label"
                (input)="label.set($any($event.target).value); problem.set('')"
                (keydown.escape)="$event.stopPropagation(); renaming.set(null)"
              />
              @if (problem()) {
                <p class="se-error">{{ problem() }}</p>
              }
              <div class="flex gap-1.5">
                <button type="submit" class="se-btn se-btn-primary">Save</button>
                <button type="button" class="se-btn" (click)="renaming.set(null)">Cancel</button>
              </div>
            </form>
          } @else {
            <div class="se-menu-row">
              <button
                type="button"
                class="se-pop-item"
                role="menuitem"
                [attr.aria-current]="container.id === store.place()?.container?.id"
                [disabled]="container.scenes.length === 0"
                (click)="goTo(container)"
              >
                <span class="se-pop-label">{{ container.label }}</span>
                <span class="se-pop-meta">{{ scenes(container) }}</span>
                <span class="se-pop-check">{{
                  container.id === store.place()?.container?.id ? '✓' : ''
                }}</span>
              </button>
              <button
                type="button"
                class="se-btn se-btn-text px-2 text-[12px]"
                (click)="startRename(container)"
              >
                Rename
              </button>
            </div>
          }
        }
        <form class="se-menu-form" (submit)="$event.preventDefault(); create()">
          <span class="se-kicker">New container</span>
          <div class="flex gap-1.5">
            <input
              class="se-field"
              aria-label="New container label"
              placeholder="e.g. Act II, Part Two, Episode 4"
              [attr.maxlength]="max"
              [value]="newLabel()"
              (input)="newLabel.set($any($event.target).value); createProblem.set('')"
            />
            <button type="submit" class="se-btn se-btn-primary" [disabled]="!newLabel().trim()">
              Add
            </button>
          </div>
          @if (createProblem()) {
            <p class="se-error">{{ createProblem() }}</p>
          }
          <p class="m-0 text-[11.5px] text-[var(--se-muted-2)]">
            It starts with one untitled scene.
          </p>
        </form>
      </div>
    }
  `,
})
export class ContainerMenu {
  protected readonly store = inject(SceneEditorStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly max = NAME_MAX;
  protected readonly open = signal(false);
  protected readonly renaming = signal<string | null>(null);
  protected readonly label = signal('');
  protected readonly problem = signal('');
  protected readonly newLabel = signal('');
  protected readonly createProblem = signal('');

  protected readonly containers = computed(() => this.store.story()?.containers ?? []);

  protected toggle(): void {
    this.open.update((open) => !open);
    this.renaming.set(null);
  }

  protected scenes(container: Container): string {
    return plural(container.scenes.length, 'scene');
  }

  protected goTo(container: Container): void {
    const first = container.scenes[0];
    if (first) {
      this.store.go(first.id);
      this.open.set(false);
    }
  }

  protected startRename(container: Container): void {
    this.renaming.set(container.id);
    this.label.set(container.label);
    this.problem.set('');
    setTimeout(() =>
      this.host.nativeElement.querySelector<HTMLInputElement>('.se-menu-form input')?.select(),
    );
  }

  protected async rename(container: Container): Promise<void> {
    const label = this.label().trim();
    if (!label) {
      this.problem.set('A container needs a label.');
      return;
    }
    await this.store.renameContainer(container.id, label);
    this.renaming.set(null);
  }

  protected async create(): Promise<void> {
    const label = this.newLabel().trim();
    if (!label) {
      this.createProblem.set('A container needs a label.');
      return;
    }
    await this.store.createContainer(label);
    this.newLabel.set('');
    this.open.set(false);
  }

  protected onDocumentDown(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }
}
