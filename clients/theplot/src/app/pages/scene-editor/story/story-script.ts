import {
  afterNextRender,
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { SceneElement } from '../../../stories/model/scene-element';
import { SceneElementType } from '../../../stories/model/scene-element-type';
import { blockLength, canDrop, isBeat } from '../../../stories/scene-blocks';
import { Avatar } from '../avatar';
import { slugline } from '../format';
import { SceneEditorStore } from '../scene-editor-store';
import { ScenePlace } from '../scene-place';
import { blockLabel, downField } from './block-types';
import { caretRect } from './caret-rect';
import { FocusRequest } from './focus-request';
import { StoryBlock } from './story-block';
import { StoryEditor } from './story-editor';

const MORE: { type: SceneElementType; label: string }[] = [
  { type: 'Narration', label: 'Narration' },
  { type: 'Description', label: 'Description' },
  { type: 'Heading', label: 'Place' },
  { type: 'Transition', label: 'Transition' },
];

const MENU_WIDTH = 296;
const SPOT_SETTLE_MS = 300;

/**
 * The scene as one editable page: blocks in the director view's columns, a gap between any two
 * to add one there, a handle to drag each, and the menus the cursor's field opens.
 */
@Component({
  selector: 'app-story-script',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, StoryBlock],
  host: { class: 'dr-scroll st-scroll', '(document:mousedown)': 'onDocumentDown($event)' },
  template: `
    <div #page class="dr-page st-page">
      <div #rail class="st-rail" aria-hidden="true"></div>
      <button
        type="button"
        class="st-gap is-top"
        title="Add a block here"
        tabindex="-1"
        (mousedown)="$event.preventDefault()"
        (click)="editor.insertWithMenu(0)"
        (dragover)="onGapOver($event, 0)"
        (drop)="onDrop($event)"
      >
        <span class="st-gap-line"></span><span class="st-gap-label">+ Add a block</span>
      </button>
      @for (element of store.elements(); track element.id; let index = $index) {
        @if (index > 0) {
          <button
            type="button"
            class="st-gap"
            title="Add a block here"
            tabindex="-1"
            (mousedown)="$event.preventDefault()"
            (click)="editor.insertWithMenu(index)"
            (dragover)="onGapOver($event, index)"
            (drop)="onDrop($event)"
          >
            <span class="st-gap-line"></span><span class="st-gap-label">+ Add a block</span>
          </button>
        }
        @if (showsDrop(index)) {
          <div class="dr-drop">
            <span>{{ dragLabel() }}</span>
          </div>
        }
        <div
          class="st-row"
          [id]="'row-' + element.id"
          [attr.data-row]="element.id"
          [class.is-active]="editor.active()?.id === element.id"
          [class.is-heading]="element.type === 'Heading'"
          [class.is-new]="editor.fresh().has(element.id)"
          [class.is-dragged]="isDragged(index)"
          (dragover)="onDragOver($event, index)"
          (drop)="onDrop($event)"
        >
          <div class="st-gutter">
            <button
              type="button"
              class="st-add"
              title="Add a block below"
              aria-label="Add a block below"
              tabindex="-1"
              (mousedown)="$event.preventDefault()"
              (click)="editor.insertWithMenu(index + 1)"
            >
              +
            </button>
          </div>
          <div
            class="dr-handle st-handle"
            draggable="true"
            [title]="
              element.type === 'Heading' ? 'Drag to move this place with its beats' : 'Drag to move'
            "
            (dragstart)="onDragStart($event, element, index)"
            (dragend)="endDrag()"
          >
            ⋮
          </div>
          <div class="dr-type st-type" (mousedown)="enter($event, element)">
            {{ label(element.type) }}
          </div>
          <app-story-block class="st-text" [element]="element" />
          <div class="st-end">
            <button
              type="button"
              class="st-remove"
              title="Delete this block"
              aria-label="Delete this block"
              tabindex="-1"
              (mousedown)="$event.preventDefault()"
              (click)="editor.remove(element.id)"
            >
              ×
            </button>
          </div>
        </div>
      }
      <div
        class="st-tail"
        title="Click to keep writing"
        (mousedown)="onTail($event)"
        (dragover)="onEndDragOver($event)"
        (drop)="onDrop($event)"
      >
        @if (showsDrop(store.elements().length)) {
          <div class="dr-drop">
            <span>{{ dragLabel() }}</span>
          </div>
        }
      </div>

      <div class="dr-continue">
        <span class="dr-continue-kicker">Continue</span>
        <button type="button" class="dr-continue-btn" (click)="append('Action')">Action</button>
        <button type="button" class="dr-continue-btn" (click)="append('Dialogue')">Dialogue</button>
        <span class="dr-anchor">
          <button
            type="button"
            class="dr-continue-more"
            [attr.aria-expanded]="moreOpen()"
            (click)="moreOpen.set(!moreOpen())"
          >
            More ▾
          </button>
          @if (moreOpen()) {
            <div class="dr-menu dr-menu-up" role="menu">
              @for (option of more; track option.type) {
                <button
                  type="button"
                  class="dr-menu-item"
                  role="menuitem"
                  (click)="moreOpen.set(false); append(option.type)"
                >
                  {{ option.label }}
                </button>
              }
            </div>
          }
        </span>
        <span class="dr-continue-hint">Type / in any block for the rest</span>
      </div>

      @if (editor.menuShown()) {
        <div
          #menu
          class="st-menu"
          role="listbox"
          [attr.aria-label]="editor.menuTitle()"
          (mousedown)="$event.preventDefault()"
        >
          <div class="st-menu-head">
            <span>{{ editor.menuTitle() }}</span>
            <span class="st-menu-query">{{ menuQuery() }}</span>
          </div>
          @for (option of editor.options(); track option.key; let i = $index) {
            <button
              type="button"
              class="st-menu-item"
              role="option"
              tabindex="-1"
              [class.is-active]="i === editor.highlighted()"
              [class.is-new]="option.look === 'new'"
              [attr.aria-selected]="i === editor.highlighted()"
              (mouseenter)="editor.highlight.set(i)"
              (click)="option.run()"
            >
              @switch (option.look) {
                @case ('avatar') {
                  <app-avatar
                    [shape]="option.shape"
                    [hue]="option.hue"
                    [initials]="option.glyph"
                    [size]="26"
                  />
                }
                @case ('location') {
                  <span class="st-menu-icon is-location hue-fill" [style.--hue]="option.hue">{{
                    option.glyph
                  }}</span>
                }
                @default {
                  <span class="st-menu-icon" [class.is-prop]="option.look === 'prop'">{{
                    option.glyph
                  }}</span>
                }
              }
              <span class="st-menu-text">
                <span class="st-menu-label">{{ option.label }}</span>
                @if (option.sub) {
                  <span class="st-menu-sub">{{ option.sub }}</span>
                }
              </span>
              <kbd class="st-menu-kbd">↵</kbd>
            </button>
          } @empty {
            <div class="st-menu-empty">
              {{
                editor.textMenu()?.kind === 'slash'
                  ? 'No block matches — Esc to dismiss'
                  : 'Nothing to suggest'
              }}
            </div>
          }
        </div>
      }
    </div>

    @if (store.scene()) {
      <div class="se-end">END OF SCENE {{ sceneNumber() }}</div>
      <div class="se-scene-cards st-scene-cards">
        <button type="button" class="se-scene-card is-new" (click)="store.addScene(true)">
          <span class="se-scene-card-plus" aria-hidden="true">+</span>
          <span class="se-scene-card-text">
            <span class="text-[14px] font-semibold">New scene after</span>
            <span class="text-[12px] text-[var(--se-muted)]">{{ addAfterHint() }}</span>
          </span>
        </button>
        @if (store.next(); as next) {
          <button type="button" class="se-scene-card" (click)="store.go(next.summary.id)">
            <span class="se-scene-card-text">
              <span class="se-kicker">Next · {{ placeLabel(next) }}</span>
              <span class="text-[14px] font-semibold">{{ next.summary.title }}</span>
              @if (nextSlug()) {
                <span class="se-slug text-[12px] text-[var(--se-muted)]">{{ nextSlug() }}</span>
              }
            </span>
            <span class="text-[16px] text-[var(--se-muted)]" aria-hidden="true">→</span>
          </button>
        }
      </div>
    }
  `,
})
export class StoryScript {
  protected readonly store = inject(SceneEditorStore);
  protected readonly editor = inject(StoryEditor);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  private readonly page = viewChild.required<ElementRef<HTMLElement>>('page');
  private readonly rail = viewChild.required<ElementRef<HTMLElement>>('rail');
  private readonly menu = viewChild<ElementRef<HTMLElement>>('menu');

  protected readonly more = MORE;
  protected readonly moreOpen = signal(false);
  protected readonly dragFrom = signal<number | null>(null);
  protected readonly dropAt = signal<number | null>(null);
  private applied: FocusRequest | null = null;
  private shownSceneId: string | null = null;
  private spotTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly sceneNumber = computed(() => (this.store.place()?.index ?? 0) + 1);

  protected readonly menuQuery = computed(() => {
    const menu = this.editor.textMenu();
    if (this.editor.active()?.field !== 'text') {
      return '';
    }
    return menu && menu.start >= 0 ? (menu.kind === 'slash' ? '/' : '@') + menu.query : '';
  });

  protected readonly nextSlug = computed(() => {
    const next = this.store.next()?.summary;
    const location = next?.openingLocationId
      ? this.store.locationById().get(next.openingLocationId)
      : undefined;
    if (!location) {
      return '';
    }
    return slugline(location) + (next?.openingTimeOfDay ? ` — ${next.openingTimeOfDay}` : '');
  });

  protected readonly addAfterHint = computed(() => {
    const place = this.store.place();
    const next = this.store.next();
    if (!place) {
      return '';
    }
    const number = place.index + 2;
    return next && next.container.id === place.container.id
      ? `Becomes Scene ${number} · “${next.summary.title}” moves to ${number + 1}`
      : `Becomes Scene ${number} of ${place.container.label}`;
  });

  private readonly dragLength = computed(() => {
    const from = this.dragFrom();
    return from === null ? 0 : blockLength(this.store.elements(), from);
  });
  protected readonly dragLabel = computed(() => {
    const beats = this.dragLength() - 1;
    return beats > 0 ? `Move place + ${beats} beat${beats === 1 ? '' : 's'} here` : 'Move here';
  });

  constructor() {
    afterRenderEffect(() => this.editor.attach(this.page().nativeElement));

    effect(() => {
      const id = this.store.scene()?.id ?? null;
      if (id !== this.shownSceneId) {
        this.shownSceneId = id;
        untracked(() => {
          this.editor.reset();
          const resumeAt = id ? this.store.resumeAt(id) : null;
          afterNextRender(() => this.arrive(resumeAt), { injector: this.injector });
        });
      }
    });

    // A native listener: a template one would run change detection on every scroll event.
    const scroller = this.host.nativeElement;
    const onScroll = () => {
      clearTimeout(this.spotTimer);
      this.spotTimer = setTimeout(() => this.noteSpot(), SPOT_SETTLE_MS);
    };
    scroller.addEventListener('scroll', onScroll, { passive: true });
    inject(DestroyRef).onDestroy(() => {
      scroller.removeEventListener('scroll', onScroll);
      clearTimeout(this.spotTimer);
    });

    // Blocks opened from outside the page, such as the library's "+ Line", take the cursor.
    effect(() => {
      const id = this.store.editingId();
      untracked(() => {
        const element = this.store.elements().find((e) => e.id === id);
        if (element && this.editor.active()?.id !== id) {
          const focus = !this.store.titleRequest();
          this.editor.go(element.id, this.editor.entryField(element), focus ? 'end' : null);
        }
      });
    });

    effect(() => {
      const request = this.store.caretRequest();
      if (request) {
        untracked(() => {
          this.editor.focusRequest.set({
            id: request.elementId,
            field: 'text',
            caret: request.caret,
          });
          this.store.caretRequest.set(null);
        });
      }
    });

    afterRenderEffect(() => this.applyFocus());
    afterRenderEffect(() => this.place());
  }

  protected label(type: SceneElementType): string {
    return blockLabel(type);
  }

  protected placeLabel(place: ScenePlace): string {
    const scene = `SCENE ${place.index + 1}`;
    return place.container.id === this.store.place()?.container.id
      ? scene
      : `${place.container.label.toUpperCase()} · ${scene}`;
  }

  protected enter(event: MouseEvent, element: SceneElement): void {
    event.preventDefault();
    this.editor.go(element.id, downField(element.type));
  }

  protected append(type: SceneElementType): void {
    this.editor.insertAt(this.store.elements().length, type);
  }

  /** Below the last block: back into it when it is an empty beat, else a new action. */
  protected onTail(event: MouseEvent): void {
    event.preventDefault();
    const elements = this.store.elements();
    const last = elements[elements.length - 1];
    if (last && isBeat(last.type) && last.type !== 'Dialogue' && !last.text) {
      this.editor.go(last.id, 'text');
    } else {
      this.append('Action');
    }
  }

  /** A scene that just opened shows its top, or puts the block it was left at on top. */
  private arrive(elementId: string | null): void {
    const scroller = this.host.nativeElement;
    const row = elementId
      ? this.page().nativeElement.querySelector<HTMLElement>(`[data-row="${elementId}"]`)
      : null;
    scroller.scrollTop = row
      ? scroller.scrollTop + row.getBoundingClientRect().top - scroller.getBoundingClientRect().top
      : 0;
    this.noteSpot();
  }

  /** The first block not scrolled past; `arrive` puts it back on top. */
  private noteSpot(): void {
    const top = this.host.nativeElement.getBoundingClientRect().top;
    const rows = [...this.page().nativeElement.querySelectorAll<HTMLElement>('[data-row]')];
    const index = rows.findIndex((row) => row.getBoundingClientRect().bottom > top);
    const at = index === -1 ? rows.length - 1 : index;
    this.store.keepSpot(at > 0 ? rows[at].dataset['row']! : null);
  }

  private applyFocus(): void {
    const request = this.editor.focusRequest();
    this.store.elements();
    this.editor.active();
    if (!request || request === this.applied) {
      return;
    }
    const field = this.page().nativeElement.querySelector<HTMLInputElement | HTMLTextAreaElement>(
      `[data-f="${request.id}:${request.field}"]`,
    );
    if (!field) {
      return;
    }
    this.applied = request;
    if (document.activeElement !== field) {
      field.focus({ preventScroll: true });
    }
    const length = field.value.length;
    const caret = request.caret;
    if (caret === 'all') {
      field.select();
    } else {
      const at = caret === 'start' ? 0 : caret === 'end' ? length : Math.min(caret, length);
      field.setSelectionRange(at, at);
    }
    document
      .getElementById(`row-${request.id}`)
      ?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }

  /** The highlight behind the cursor's block and the menu under its field, measured after render. */
  private place(): void {
    const active = this.editor.active();
    const menuOpen = this.editor.menuShown();
    this.store.elements();
    this.editor.textMenu();
    this.editor.options();
    const page = this.page().nativeElement;
    const rail = this.rail().nativeElement;
    const row = active && page.querySelector<HTMLElement>(`[data-row="${active.id}"]`);
    if (row) {
      rail.style.transform = `translateY(${row.offsetTop}px)`;
      rail.style.height = `${row.offsetHeight}px`;
      rail.classList.add('is-shown');
    } else {
      rail.classList.remove('is-shown');
    }
    const menu = this.menu()?.nativeElement;
    if (!menu || !active || !menuOpen) {
      return;
    }
    const anchor = this.anchor(page, active.id, active.field);
    if (!anchor) {
      return;
    }
    const box = page.getBoundingClientRect();
    const view = this.host.nativeElement.getBoundingClientRect();
    const left = Math.max(8, Math.min(anchor.left - box.left - 8, box.width - MENU_WIDTH - 8));
    const below = anchor.bottom + 6;
    const fits =
      below + menu.offsetHeight < view.bottom - 8 || anchor.top - menu.offsetHeight < view.top;
    const top = fits ? below - box.top : anchor.top - 6 - menu.offsetHeight - box.top;
    menu.style.left = `${left}px`;
    menu.style.top = `${top}px`;
    menu.classList.add('is-placed');
  }

  private anchor(page: HTMLElement, id: string, field: string): DOMRect | null {
    if (field !== 'text') {
      return page.querySelector(`[data-f="${id}:${field}"]`)?.getBoundingClientRect() ?? null;
    }
    const copy = page.querySelector<HTMLElement>(`[data-copy="${id}"]`);
    const menu = this.editor.textMenu();
    return copy && menu ? caretRect(copy, Math.max(menu.start, 0)) : null;
  }

  protected onDocumentDown(event: MouseEvent): void {
    const more = this.host.nativeElement.querySelector('.dr-continue .dr-anchor');
    if (this.moreOpen() && !more?.contains(event.target as Node)) {
      this.moreOpen.set(false);
    }
  }

  // ─── drag to reorder ───────────────────────────────────────────────────

  protected onDragStart(event: DragEvent, element: SceneElement, index: number): void {
    event.stopPropagation();
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData('text/plain', element.id);
      const row = document.getElementById(`row-${element.id}`);
      if (row) {
        event.dataTransfer.setDragImage(row, 24, 18);
      }
    }
    // After the browser has taken its drag image, so the row isn't faded in it.
    setTimeout(() => {
      this.dragFrom.set(index);
      this.dropAt.set(null);
      this.editor.textMenu.set(null);
    });
  }

  protected onDragOver(event: DragEvent, index: number): void {
    if (this.dragFrom() === null) {
      return;
    }
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.dropAt.set(event.clientY < rect.top + rect.height / 2 ? index : index + 1);
  }

  protected onGapOver(event: DragEvent, index: number): void {
    if (this.dragFrom() !== null) {
      event.preventDefault();
      this.dropAt.set(index);
    }
  }

  protected onEndDragOver(event: DragEvent): void {
    if (this.dragFrom() !== null) {
      event.preventDefault();
      this.dropAt.set(this.store.elements().length);
    }
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    const from = this.dragFrom();
    const at = this.dropAt();
    if (from !== null && at !== null) {
      this.store.dropBlock(from, at);
    }
    this.endDrag();
  }

  protected endDrag(): void {
    this.dragFrom.set(null);
    this.dropAt.set(null);
  }

  protected isDragged(index: number): boolean {
    const from = this.dragFrom();
    return from !== null && index >= from && index < from + this.dragLength();
  }

  protected showsDrop(at: number): boolean {
    const from = this.dragFrom();
    return from !== null && this.dropAt() === at && canDrop(this.store.elements(), from, at);
  }
}
