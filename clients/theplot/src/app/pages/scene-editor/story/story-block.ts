import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  viewChild,
} from '@angular/core';
import { sameName } from '../../../stories/library-names';
import { SceneElement } from '../../../stories/model/scene-element';
import { Avatar } from '../avatar';
import { initials, settingPrefix, transitionText } from '../format';
import { MentionText } from '../mention-text';
import { SceneEditorStore } from '../scene-editor-store';
import { BlockField } from './block-field';
import { blockLabel, PLACEHOLDERS } from './block-types';
import { MenuOption } from './menu-option';
import { StoryEditor } from './story-editor';

/** One block of the scene, laid out as the director view reads it, with every part editable. */
@Component({
  selector: 'app-story-block',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, MentionText, NgTemplateOutlet],
  template: `
    @switch (element().type) {
      @case ('Heading') {
        <div
          class="st-heading"
          [class.has-hue]="location() !== null"
          [style.--hue]="location()?.hue ?? null"
        >
          <span
            class="st-part"
            [class.is-empty]="!setting()"
            [class.is-on]="field() === 'setting'"
            (mousedown)="go($event, 'setting')"
            >{{ setting() || 'INT./EXT.' }}</span
          >
          @if (field() === 'place') {
            <input
              class="st-inline st-place-input"
              placeholder="WHERE?"
              autocomplete="off"
              spellcheck="false"
              [attr.data-f]="key('place')"
              [style.--chars]="width(editor.query(), 6)"
              [value]="editor.query()"
              (input)="editor.setQuery(value($event))"
              (keydown)="editor.onPickKey(id(), 'place', $event)"
              (focus)="editor.onFocusField(id(), 'place')"
            />
          } @else {
            <span
              class="st-part st-place"
              [class.is-empty]="!location()"
              (mousedown)="go($event, 'place')"
              >{{ location()?.name ?? 'Location' }}</span
            >
          }
          <span class="st-dash">—</span>
          <span
            class="st-part"
            [class.is-empty]="!element().timeOfDay"
            [class.is-on]="field() === 'time'"
            (mousedown)="go($event, 'time')"
            >{{ element().timeOfDay ?? 'TIME' }}</span
          >
        </div>
        @if (field() === 'setting' || field() === 'time') {
          <ng-container
            [ngTemplateOutlet]="chips"
            [ngTemplateOutletContext]="{ $implicit: field() === 'setting' ? 'Setting' : 'Time' }"
          />
        }
      }
      @case ('Transition') {
        <div class="st-transition">
          @if (field() === 'trans') {
            <input
              class="st-inline"
              placeholder="CUT TO:"
              autocomplete="off"
              spellcheck="false"
              [attr.data-f]="key('trans')"
              [style.--chars]="width(editor.query(), 7)"
              [value]="editor.query()"
              (input)="editor.setQuery(value($event))"
              (keydown)="editor.onPickKey(id(), 'trans', $event)"
              (focus)="editor.onFocusField(id(), 'trans')"
            />
          } @else {
            <span
              class="st-part"
              [class.is-empty]="!element().transition"
              (mousedown)="go($event, 'trans')"
              >{{ transition() }}</span
            >
          }
        </div>
      }
      @case ('Dialogue') {
        <div class="st-dialogue">
          @if (field() === 'speaker') {
            <span class="st-speaker" [style.--hue]="typed()?.hue ?? null">
              <app-avatar
                class="st-cue-avatar"
                [shape]="typed() ? store.kindOf(typed()!).avatarShape : 'Circle'"
                [hue]="typed()?.hue ?? null"
                [initials]="typed() ? initialsOf(typed()!.name) : editor.query() ? '+' : '?'"
                [size]="18"
              />
              <input
                class="st-inline st-speaker-input"
                placeholder="WHO SPEAKS?"
                autocomplete="off"
                spellcheck="false"
                [class.hue-text]="typed() !== null"
                [attr.data-f]="key('speaker')"
                [style.--chars]="width(editor.query(), 11)"
                [value]="editor.query()"
                (input)="editor.setQuery(value($event))"
                (keydown)="editor.onPickKey(id(), 'speaker', $event)"
                (focus)="editor.onFocusField(id(), 'speaker')"
              />
            </span>
          } @else {
            <span
              class="st-speaker"
              [style.--hue]="speaker()?.hue ?? null"
              (mousedown)="go($event, 'speaker')"
            >
              <app-avatar
                class="st-cue-avatar"
                [shape]="speaker() ? store.kindOf(speaker()!).avatarShape : 'Circle'"
                [hue]="speaker()?.hue ?? null"
                [initials]="speaker() ? initialsOf(speaker()!.name) : '?'"
                [size]="18"
              />
              <span [class.hue-text]="speaker() !== null" [class.is-empty]="!speaker()">{{
                speaker()?.name?.toUpperCase() ?? 'SPEAKER'
              }}</span>
              @if (element().extension && field() !== 'ext') {
                <span class="st-ext">({{ element().extension }})</span>
              }
            </span>
          }
          @if (field() === 'ext') {
            <ng-container
              [ngTemplateOutlet]="chips"
              [ngTemplateOutletContext]="{ $implicit: 'Extension' }"
            />
          }
          @if (field() !== null || element().parenthetical) {
            <span class="st-paren" [class.is-empty]="!element().parenthetical">
              (<input
                class="st-inline"
                placeholder="parenthetical"
                autocomplete="off"
                spellcheck="false"
                [attr.data-f]="key('paren')"
                [style.--chars]="width(element().parenthetical ?? '', 13)"
                [value]="element().parenthetical ?? ''"
                (input)="editor.onParenInput(id(), value($event))"
                (keydown)="editor.onParenKey(id(), $event)"
                (focus)="editor.onFocusField(id(), 'paren')"
                (blur)="editor.commitParen(id())"
              />)
            </span>
          }
          <div class="st-line">
            <ng-container [ngTemplateOutlet]="text" />
          </div>
        </div>
      }
      @default {
        <div class="st-action" [class.is-description]="element().type === 'Description'">
          <ng-container [ngTemplateOutlet]="text" />
        </div>
      }
    }

    <ng-template #text>
      <div class="st-field">
        <div #copy class="st-copy" aria-hidden="true" [attr.data-copy]="id()">
          <app-mention-text
            [text]="element().text ?? ''"
            [mentions]="element().mentions"
            [marks]="true"
          /><br />
        </div>
        <textarea
          class="st-textarea"
          rows="1"
          spellcheck="false"
          [attr.data-f]="key('text')"
          [attr.aria-label]="label() + ' text'"
          [attr.placeholder]="field() === 'text' ? placeholder() : null"
          [value]="element().text ?? ''"
          (input)="editor.onTextInput(id(), $any($event.target))"
          (keydown)="onTextKey($event, $any($event.target))"
          (keyup)="editor.onCaretMove(id(), $any($event.target))"
          (click)="editor.onCaretMove(id(), $any($event.target))"
          (select)="editor.trackCaret($any($event.target))"
          (focus)="editor.onFocusField(id(), 'text')"
          (blur)="editor.closeTextMenu(id())"
        ></textarea>
      </div>
    </ng-template>

    <ng-template #chips let-title>
      <div class="st-chips">
        <span class="st-chips-title">{{ title }}</span>
        @for (option of editor.options(); track option.key; let i = $index) {
          <button
            type="button"
            class="st-chip"
            tabindex="-1"
            [class.is-on]="i === editor.highlighted()"
            [class.is-new]="option.look === 'new'"
            (mousedown)="pick($event, option)"
          >
            {{ option.label }}
          </button>
        }
        <input
          class="st-chip-input"
          placeholder="type to filter"
          autocomplete="off"
          spellcheck="false"
          [attr.data-f]="key(field()!)"
          [value]="editor.query()"
          (input)="editor.setQuery(value($event))"
          (keydown)="editor.onPickKey(id(), field()!, $event)"
          (focus)="editor.onFocusField(id(), field()!)"
        />
      </div>
    </ng-template>
  `,
})
export class StoryBlock {
  protected readonly store = inject(SceneEditorStore);
  protected readonly editor = inject(StoryEditor);

  readonly element = input.required<SceneElement>();

  private readonly copy = viewChild<ElementRef<HTMLElement>>('copy');

  protected readonly id = computed(() => this.element().id);
  protected readonly field = computed<BlockField | null>(() => {
    const active = this.editor.active();
    return active?.id === this.element().id ? active.field : null;
  });
  protected readonly label = computed(() => blockLabel(this.element().type));
  protected readonly placeholder = computed(() => PLACEHOLDERS[this.element().type] ?? '');
  protected readonly speaker = computed(() => this.editor.speakerOf(this.element()));
  protected readonly location = computed(() => this.editor.locationOf(this.element()));
  /** The character the speaker field's text names, while it is typed. */
  protected readonly typed = computed(() => {
    const query = this.editor.query();
    return this.store.characters().find((c) => sameName(c.name, query)) ?? null;
  });
  protected readonly setting = computed(() => {
    const setting =
      this.location()?.setting ?? (this.field() ? this.editor.pendingSetting() : null);
    return setting ? settingPrefix(setting) : '';
  });
  protected readonly transition = computed(() => {
    const transition = this.element().transition;
    return transition ? transitionText(transition) : 'TRANSITION';
  });

  protected key(field: BlockField): string {
    return `${this.element().id}:${field}`;
  }

  /** In characters: the fields are monospaced, so the text, or else the placeholder, sets it. */
  protected width(text: string, placeholder: number): number {
    return text.length || placeholder;
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected initialsOf(name: string): string {
    return initials(name);
  }

  protected go(event: MouseEvent, field: BlockField): void {
    event.preventDefault();
    this.editor.go(this.element().id, field);
  }

  protected pick(event: MouseEvent, option: MenuOption): void {
    event.preventDefault();
    option.run();
  }

  protected onTextKey(event: KeyboardEvent, field: HTMLTextAreaElement): void {
    if (
      event.key === '@' &&
      !event.ctrlKey &&
      !event.metaKey &&
      this.editor.tagSelection(this.element().id, field)
    ) {
      event.preventDefault();
      return;
    }
    const copy = this.copy()?.nativeElement;
    if (copy) {
      this.editor.onTextKey(this.element().id, event, field, copy);
    }
  }
}
