import { computed, inject, Injectable, signal } from '@angular/core';
import { sameName } from '../../../stories/library-names';
import {
  applyTyping,
  insertReference,
  joinText,
  linkTypedReferences,
  matchName,
  REFERENCE_ORDER,
  sliceText,
  tagReference,
  wordStart,
} from '../../../stories/mentions';
import { Character } from '../../../stories/model/character';
import { LocationSetting } from '../../../stories/model/location-setting';
import { MentionedText } from '../../../stories/model/mentioned-text';
import { Referable } from '../../../stories/model/referable';
import { ReferenceInsertion } from '../../../stories/model/reference-insertion';
import { SceneElement } from '../../../stories/model/scene-element';
import { SceneElementType } from '../../../stories/model/scene-element-type';
import { isBeat } from '../../../stories/scene-blocks';
import { initials, parseLocationQuery, plural, settingPrefix, transitionText } from '../format';
import { SceneEditorStore } from '../scene-editor-store';
import { BlockField } from './block-field';
import { BlockFocus } from './block-focus';
import { BLOCK_TYPES, CONVERTIBLE, downField, fieldsOf, lastField } from './block-types';
import { onFirstLine, onLastLine } from './caret-rect';
import { FocusRequest } from './focus-request';
import { MenuOption } from './menu-option';
import { TextMenu } from './text-menu';

const SETTINGS: { label: string; setting: LocationSetting }[] = [
  { label: 'INT.', setting: 'Interior' },
  { label: 'EXT.', setting: 'Exterior' },
];
const NO_EXTENSION = 'None';
/** ⌥1–4. */
const BY_DIGIT: SceneElementType[] = ['Action', 'Dialogue', 'Description', 'Narration'];
const CHIP_FIELDS: ReadonlySet<BlockField> = new Set(['ext', 'setting', 'time']);

/** An `@` and up to 24 characters of name typed since. */
const MENTION_QUERY = /@([^@\n]{0,24})$/;
/** Before an `@`, these start a mention; a word character instead makes it a tag (`him@`). */
const MENTION_LEAD = /[\s(“"‘']/;
const WORD_CHAR = /[\p{L}\p{N}]/u;
const ENDS_OFF_WORD = /[^\p{L}\p{N}]$/u;
const SLASH_QUERY = /(^|\s)\/(\p{L}*)$/u;

function norm(text: string): string {
  return text.toLowerCase().replace(/[^\p{L}\p{N}]/gu, '');
}

/** Prefix matches first, then matches inside. */
function rank<T>(list: readonly T[], query: string, label: (item: T) => string): T[] {
  const needle = norm(query);
  if (!needle) {
    return [...list];
  }
  const starts: T[] = [];
  const inside: T[] = [];
  for (const item of list) {
    const text = norm(label(item));
    if (text.startsWith(needle)) {
      starts.push(item);
    } else if (text.includes(needle)) {
      inside.push(item);
    }
  }
  return [...starts, ...inside];
}

function titleCase(text: string): string {
  return text
    .trim()
    .replace(/\s+/g, ' ')
    .replace(/(^|\s)(\p{L})/gu, (_, lead: string, letter: string) => lead + letter.toUpperCase());
}

function textOf(element: SceneElement): MentionedText {
  return { text: element.text ?? '', mentions: element.mentions };
}

function sameMenu(a: TextMenu | null, b: TextMenu | null): boolean {
  return (
    a === b ||
    (!!a &&
      !!b &&
      a.kind === b.kind &&
      a.start === b.start &&
      a.query === b.query &&
      a.tagStart === b.tagStart)
  );
}

function option(
  key: string,
  label: string,
  sub: string,
  run: () => void,
  look: Partial<MenuOption> = {},
): MenuOption {
  return { key, label, sub, look: 'glyph', glyph: '', hue: null, shape: 'Circle', run, ...look };
}

/**
 * The story editor's cursor: which block and which of its fields has the caret, the menu that
 * field opens, and the keyboard path through a scene. Every block is always editable; the
 * cursor only decides which field is live.
 */
@Injectable()
export class StoryEditor {
  private readonly store = inject(SceneEditorStore);
  private page: HTMLElement | null = null;

  readonly active = signal<BlockFocus | null>(null);
  /** What a picker field holds while it is typed in. */
  readonly query = signal('');
  readonly highlight = signal(0);
  readonly textMenu = signal<TextMenu | null>(null);
  readonly focusRequest = signal<FocusRequest | null>(null);
  /** Blocks added this visit and not yet finished: finishing one adds the next block. */
  readonly fresh = signal<ReadonlySet<string>>(new Set());
  /** INT./EXT. picked before a heading has a place, for the place it then adds. */
  readonly pendingSetting = signal<LocationSetting | null>(null);
  /** The `/` or `@` whose menu Escape closed, so it stays closed. */
  private dismissed = -1;

  readonly activeElement = computed(() => {
    const active = this.active();
    return active ? (this.store.elements().find((e) => e.id === active.id) ?? null) : null;
  });

  readonly options = computed<MenuOption[]>(() => {
    const active = this.active();
    const element = this.activeElement();
    return active && element ? this.optionsFor(element, active.field) : [];
  });

  readonly highlighted = computed(() =>
    Math.min(this.highlight(), Math.max(0, this.options().length - 1)),
  );

  /** A list under the field; chip steps show theirs inline instead. */
  readonly menuShown = computed(() => {
    const active = this.active();
    const element = this.activeElement();
    if (!active || !element) {
      return false;
    }
    switch (active.field) {
      case 'text':
        return this.textMenu() !== null;
      case 'speaker':
        return this.query() !== (this.speakerOf(element)?.name ?? null);
      case 'place':
        return this.query() !== (this.locationOf(element)?.name ?? null);
      case 'trans':
        return !element.transition || this.query() !== '';
      default:
        return false;
    }
  });

  readonly menuTitle = computed(() => {
    const active = this.active();
    const menu = this.textMenu();
    switch (active?.field) {
      case 'text':
        if (menu?.kind === 'slash') {
          return 'Blocks';
        }
        return menu?.tagStart != null ? 'Tag as' : 'Mention';
      case 'speaker':
        return 'Who speaks';
      case 'place':
        return 'Location';
      case 'trans':
        return 'Transition';
      default:
        return '';
    }
  });

  attach(page: HTMLElement | null): void {
    this.page = page;
  }

  reset(): void {
    this.active.set(null);
    this.textMenu.set(null);
    this.query.set('');
    this.fresh.set(new Set());
    this.pendingSetting.set(null);
  }

  // ─── moving the cursor ─────────────────────────────────────────────────

  go(
    id: string,
    field: BlockField,
    caret: FocusRequest['caret'] | null = 'end',
    prune = true,
  ): void {
    const previous = this.active();
    if (previous && previous.id !== id) {
      this.pendingSetting.set(null);
      if (prune) {
        this.prune(previous.id);
      }
    }
    const element = this.find(id);
    let query = '';
    if (element && field === 'speaker') {
      query = this.speakerOf(element)?.name ?? '';
    } else if (element && field === 'place') {
      query = this.locationOf(element)?.name ?? '';
    }
    if (caret !== null && query) {
      caret = 'all';
    }
    this.active.set({ id, field });
    this.query.set(query);
    this.highlight.set(element ? this.currentIndex(element, field) : 0);
    this.textMenu.set(null);
    this.dismissed = -1;
    this.store.editingId.set(id);
    if (caret !== null) {
      this.focusRequest.set({ id, field, caret });
    }
  }

  onFocusField(id: string, field: BlockField): void {
    const active = this.active();
    if (active?.id !== id || active.field !== field) {
      this.go(id, field, null);
    }
  }

  /** Where a block opened from outside the editor, such as the library, takes the caret. */
  entryField(element: SceneElement): BlockField {
    if (element.type === 'Dialogue' && element.speakerId) {
      return 'text';
    }
    return element.type === 'Heading' && element.locationId ? 'time' : fieldsOf(element.type)[0];
  }

  deactivate(): void {
    const active = this.active();
    if (active) {
      this.prune(active.id);
    }
    this.active.set(null);
    this.textMenu.set(null);
    this.store.editingId.set(null);
  }

  next(id: string, field: BlockField): void {
    const element = this.find(id);
    if (!element) {
      return;
    }
    const fields = fieldsOf(element.type);
    const at = fields.indexOf(field);
    if (at < fields.length - 1) {
      this.go(id, fields[at + 1]);
    } else {
      this.finish(id);
    }
  }

  private previousField(id: string, field: BlockField): void {
    const element = this.find(id);
    if (!element) {
      return;
    }
    const fields = fieldsOf(element.type);
    const at = fields.indexOf(field);
    if (at > 0) {
      this.go(id, fields[at - 1], 'end');
    } else {
      this.goPrevious(id);
    }
  }

  private goPrevious(id: string): void {
    const previous = this.store.elements()[this.index(id) - 1];
    if (previous) {
      this.go(previous.id, lastField(previous.type), 'end');
    }
  }

  private goNext(id: string): void {
    const next = this.store.elements()[this.index(id) + 1];
    if (next) {
      this.go(next.id, downField(next.type), 'start');
    }
  }

  /** A finished block hands on to the next one; a new one adds an action after it. */
  private finish(id: string): void {
    const next = this.store.elements()[this.index(id) + 1];
    if (!this.fresh().has(id) && next) {
      this.go(next.id, downField(next.type), 'end');
      return;
    }
    this.unmarkFresh(id);
    this.insertAfter(id, 'Action');
  }

  // ─── adding, removing, retyping ────────────────────────────────────────

  insertAt(
    at: number,
    type: SceneElementType,
    patch: Partial<SceneElement> = {},
    caret: FocusRequest['caret'] = 'end',
  ): SceneElement | null {
    const created = this.store.insertElement(at, type, {
      ...(type === 'Transition' ? { transition: null } : {}),
      ...patch,
    });
    if (created) {
      this.fresh.update((set) => new Set(set).add(created.id));
      this.go(created.id, fieldsOf(type)[0], caret, false);
    }
    return created;
  }

  insertAfter(
    id: string,
    type: SceneElementType,
    patch: Partial<SceneElement> = {},
    caret: FocusRequest['caret'] = 'end',
  ): void {
    this.insertAt(this.index(id) + 1, type, patch, caret);
  }

  /** An empty action at a gap, with the block menu open on it so the mouse can pick its type. */
  insertWithMenu(at: number): void {
    const created = this.insertAt(at, 'Action');
    if (created) {
      this.textMenu.set({ kind: 'slash', start: -1, query: '', tagStart: null });
      this.highlight.set(0);
    }
  }

  remove(id: string): void {
    const at = this.index(id);
    const neighbour = this.store.elements()[at - 1] ?? this.store.elements()[at + 1];
    this.store.removeElement(id);
    this.unmarkFresh(id);
    if (this.active()?.id === id) {
      this.active.set(null);
      if (neighbour) {
        this.go(neighbour.id, lastField(neighbour.type), 'end', false);
      }
    }
  }

  setType(id: string, type: SceneElementType): void {
    this.store.changeType(id, type);
    this.go(id, type === 'Dialogue' ? 'speaker' : 'text', 'end');
  }

  private cycle(id: string, back: boolean): void {
    const element = this.find(id);
    const at = element ? CONVERTIBLE.indexOf(element.type) : -1;
    if (at >= 0) {
      const step = back ? CONVERTIBLE.length - 1 : 1;
      this.setType(id, CONVERTIBLE[(at + step) % CONVERTIBLE.length]);
    }
  }

  private move(id: string, delta: -1 | 1, field: HTMLInputElement | HTMLTextAreaElement): void {
    const active = this.active();
    this.store.moveElement(id, delta);
    if (active) {
      this.focusRequest.set({ ...active, caret: field.selectionStart ?? 'end' });
    }
  }

  /** ⌥↑ ⌥↓ move the block, ⌥1–4 retype it. */
  private altKeys(
    id: string,
    event: KeyboardEvent,
    field: HTMLInputElement | HTMLTextAreaElement,
  ): boolean {
    if (event.key === 'ArrowUp' || event.key === 'ArrowDown') {
      event.preventDefault();
      this.move(id, event.key === 'ArrowUp' ? -1 : 1, field);
      return true;
    }
    const digit = /^Digit([1-4])$/.exec(event.code);
    const element = this.find(id);
    if (digit && element && CONVERTIBLE.includes(element.type)) {
      event.preventDefault();
      this.setType(id, BY_DIGIT[+digit[1] - 1]);
      return true;
    }
    return false;
  }

  /** Only a block added this visit and left empty goes. */
  private prune(id: string): void {
    const element = this.find(id);
    if (
      element &&
      this.fresh().has(id) &&
      isBeat(element.type) &&
      element.type !== 'Dialogue' &&
      !element.text &&
      this.store.elements().length > 1
    ) {
      this.store.removeElement(id);
      this.unmarkFresh(id);
    }
  }

  private unmarkFresh(id: string): void {
    if (this.fresh().has(id)) {
      this.fresh.update((set) => {
        const next = new Set(set);
        next.delete(id);
        return next;
      });
    }
  }

  // ─── the text field ────────────────────────────────────────────────────

  onTextInput(id: string, field: HTMLTextAreaElement): void {
    const element = this.find(id);
    if (!element) {
      return;
    }
    const caret = field.selectionStart;
    const previous = this.textMenu();
    const live = previous && previous.start >= 0 ? previous : null;
    const slash = this.findSlash(field.value, caret);
    const candidate = slash ? null : this.findMention(field.value, caret, live);
    const referables = this.store.referables();
    let value = applyTyping(
      textOf(element),
      field.value,
      referables,
      caret,
      candidate?.start ?? null,
    );
    const mention =
      candidate && !value.mentions.some((m) => m.offset === candidate.start) ? candidate : null;
    this.setTextMenu(slash ?? mention);
    if (live?.kind === 'mention' && live.start !== mention?.start) {
      if (live.tagStart === null) {
        value = linkTypedReferences(value, referables, live.start, live.start);
      } else if (caret > live.start) {
        const tagged = this.linkTypedTag(value, live);
        if (tagged) {
          const removed = value.text.length - tagged.value.text.length;
          this.write(
            id,
            tagged.value,
            caret >= tagged.caret + removed ? caret - removed : tagged.caret,
          );
          return;
        }
      }
    }
    this.store.setText(id, value);
    this.trackCaret(field);
  }

  /** A caret moved off the `/` or `@` being typed closes its menu. */
  onCaretMove(id: string, field: HTMLTextAreaElement): void {
    this.trackCaret(field);
    const menu = this.textMenu();
    if (!menu || menu.start < 0) {
      return;
    }
    const caret = field.selectionStart;
    if (
      field.selectionEnd !== caret ||
      caret <= menu.start ||
      caret > menu.start + 1 + menu.query.length
    ) {
      this.closeTextMenu(id);
    }
  }

  trackCaret(field: HTMLTextAreaElement): void {
    this.store.caret.set(field.selectionStart);
    this.store.selectionEnd.set(field.selectionEnd);
  }

  /** A mention closed without a pick still links a name typed out in full. */
  closeTextMenu(id: string, dismiss = false): void {
    const menu = this.textMenu();
    if (!menu) {
      return;
    }
    if (dismiss) {
      this.dismissed = menu.start;
    }
    this.textMenu.set(null);
    const element = this.find(id);
    if (menu.kind !== 'mention' || !element) {
      return;
    }
    const current = textOf(element);
    if (menu.tagStart !== null) {
      const tagged = this.linkTypedTag(current, menu);
      if (tagged) {
        this.write(id, tagged.value, tagged.caret);
      }
      return;
    }
    const value = linkTypedReferences(current, this.store.referables(), menu.start, menu.start);
    if (value !== current) {
      this.store.setText(id, value);
    }
  }

  /** An `@` typed over a selection tags it: the `@` goes after the selection to search from. */
  tagSelection(id: string, field: HTMLTextAreaElement): boolean {
    const element = this.find(id);
    const { selectionStart: from, selectionEnd: to } = field;
    if (!element || to <= from) {
      return false;
    }
    const current = textOf(element);
    const text = current.text.slice(0, to) + '@' + current.text.slice(to);
    this.write(id, applyTyping(current, text, [], to + 1), to + 1);
    this.setTextMenu({ kind: 'mention', start: to, query: '', tagStart: from });
    return true;
  }

  onTextKey(id: string, event: KeyboardEvent, field: HTMLTextAreaElement, copy: HTMLElement): void {
    const element = this.find(id);
    if (!element) {
      return;
    }
    const key = event.key;
    const menu = this.textMenu();
    if (menu) {
      const options = this.options();
      const count = options.length;
      if (key === 'ArrowDown' || key === 'ArrowUp') {
        event.preventDefault();
        if (count) {
          const step = key === 'ArrowDown' ? 1 : count - 1;
          this.highlight.set((this.highlighted() + step) % count);
        }
        return;
      }
      if (key === 'Enter' || key === 'Tab') {
        if (count) {
          event.preventDefault();
          options[this.highlighted()].run();
          return;
        }
        if (key === 'Tab') {
          event.preventDefault();
          return;
        }
        this.closeTextMenu(id);
      }
      if (key === 'Escape') {
        event.preventDefault();
        this.closeTextMenu(id, true);
        return;
      }
    }
    if (event.altKey && this.altKeys(id, event, field)) {
      return;
    }
    const start = field.selectionStart;
    const end = field.selectionEnd;
    if (key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      const current = textOf(element);
      const rest = sliceText(current, end);
      this.store.setText(id, sliceText(current, 0, start));
      this.insertAfter(id, 'Action', { text: rest.text, mentions: rest.mentions }, 'start');
      return;
    }
    if (key === 'Backspace' && start === 0 && end === 0) {
      this.backspaceAtStart(element, event);
      return;
    }
    if (key === 'Tab') {
      event.preventDefault();
      this.cycle(id, event.shiftKey);
      return;
    }
    if (key === 'ArrowUp' && !event.shiftKey && onFirstLine(field, copy)) {
      event.preventDefault();
      if (element.type === 'Dialogue') {
        this.go(id, 'paren', 'end');
      } else {
        this.goPrevious(id);
      }
      return;
    }
    if (key === 'ArrowDown' && !event.shiftKey && onLastLine(field, copy)) {
      if (this.store.elements()[this.index(id) + 1]) {
        event.preventDefault();
        this.goNext(id);
      }
      return;
    }
    if (key === 'Escape') {
      event.preventDefault();
      field.blur();
      this.deactivate();
    }
  }

  /** Steps back into the cue, removes an empty block, or joins this one onto the text above. */
  private backspaceAtStart(element: SceneElement, event: KeyboardEvent): void {
    const id = element.id;
    if (element.type === 'Dialogue') {
      event.preventDefault();
      this.go(id, 'paren', 'end');
      return;
    }
    const previous = this.store.elements()[this.index(id) - 1];
    if (!previous) {
      return;
    }
    if (!element.text) {
      event.preventDefault();
      this.remove(id);
      return;
    }
    if (isBeat(previous.type) && previous.type !== 'Dialogue') {
      event.preventDefault();
      const join = previous.text?.length ?? 0;
      this.store.setText(previous.id, joinText(textOf(previous), textOf(element)));
      this.store.removeElement(id);
      this.unmarkFresh(id);
      this.go(previous.id, 'text', join, false);
    }
  }

  // ─── picker fields ─────────────────────────────────────────────────────

  setQuery(value: string): void {
    this.query.set(value);
    this.highlight.set(0);
  }

  onPickKey(id: string, field: BlockField, event: KeyboardEvent): void {
    const element = this.find(id);
    const input = event.target as HTMLInputElement;
    if (!element) {
      return;
    }
    const key = event.key;
    const options = this.options();
    const count = options.length;
    const chips = CHIP_FIELDS.has(field);
    const shown = chips || this.menuShown();
    if (event.altKey && this.altKeys(id, event, input)) {
      return;
    }
    if (key === 'Tab' && element.type === 'Dialogue') {
      event.preventDefault();
      this.cycle(id, event.shiftKey);
      return;
    }
    if (!shown && (key === 'ArrowUp' || key === 'ArrowDown')) {
      event.preventDefault();
      if (key === 'ArrowUp') {
        this.goPrevious(id);
      } else if (field === 'speaker') {
        this.go(id, 'paren', 'end');
      } else {
        this.goNext(id);
      }
      return;
    }
    if (key === 'ArrowDown' || (chips && key === 'ArrowRight')) {
      event.preventDefault();
      if (count) {
        this.highlight.set((this.highlighted() + 1) % count);
      }
      return;
    }
    if (key === 'ArrowUp' || (chips && key === 'ArrowLeft')) {
      event.preventDefault();
      if (count) {
        this.highlight.set((this.highlighted() - 1 + count) % count);
      }
      return;
    }
    if ((key === 'Tab' && !event.shiftKey) || key === 'Enter') {
      event.preventDefault();
      const picked = options[this.highlighted()];
      if (picked) {
        picked.run();
      } else {
        this.next(id, field);
      }
      return;
    }
    if (key === 'Tab') {
      event.preventDefault();
      this.previousField(id, field);
      return;
    }
    if (key === 'Backspace' && !this.query()) {
      event.preventDefault();
      const first = fieldsOf(element.type)[0] === field;
      if (first && !element.text && !element.locationId && !element.transition) {
        this.setType(id, 'Action');
      } else {
        this.previousField(id, field);
      }
      return;
    }
    if (key === 'Escape') {
      event.preventDefault();
      this.next(id, field);
    }
  }

  onParenInput(id: string, value: string): void {
    this.store.updateElement(id, { parenthetical: value.replace(/[()]/g, '') });
  }

  commitParen(id: string): void {
    const element = this.find(id);
    const bare = element?.parenthetical?.trim() ?? '';
    if (element && bare !== element.parenthetical) {
      this.store.updateElement(id, { parenthetical: bare || null });
    }
  }

  onParenKey(id: string, event: KeyboardEvent): void {
    const input = event.target as HTMLInputElement;
    if (event.altKey && this.altKeys(id, event, input)) {
      return;
    }
    switch (event.key) {
      case 'Tab':
        event.preventDefault();
        this.cycle(id, event.shiftKey);
        return;
      case 'Enter':
      case 'ArrowDown':
      case 'Escape':
        event.preventDefault();
        this.go(id, 'text', 'end');
        return;
      case 'ArrowUp':
        event.preventDefault();
        this.go(id, 'speaker');
        return;
      case 'Backspace':
        if (!input.value) {
          event.preventDefault();
          this.go(id, 'ext');
        }
    }
  }

  // ─── the menus ─────────────────────────────────────────────────────────

  private optionsFor(element: SceneElement, field: BlockField): MenuOption[] {
    const id = element.id;
    switch (field) {
      case 'text':
        return this.textOptions(element);
      case 'speaker':
        return this.speakerOptions(element);
      case 'place':
        return this.placeOptions(element);
      case 'ext':
        return [
          ...rank([NO_EXTENSION], this.query(), (l) => l).map((l) =>
            option(l, l, '', () => this.pickExtension(id, null)),
          ),
          ...this.labelOptions(
            this.store.extensions(),
            (label) => this.pickExtension(id, label),
            (label) => this.store.addExtension(label),
          ),
        ];
      case 'setting':
        return rank(SETTINGS, this.query(), (s) => s.label).map((s) =>
          option(s.label, s.label, '', () => this.pickSetting(id, s.setting)),
        );
      case 'time':
        return this.labelOptions(
          this.store.timesOfDay(),
          (label) => this.pickTime(id, label),
          (label) => this.store.addTimeOfDay(label),
        );
      case 'trans':
        return this.labelOptions(
          this.store.transitions(),
          (label) => this.pickTransition(id, label),
          (label) => this.store.addTransition(label),
          transitionText,
        ).map((o) => ({ ...o, glyph: '→', sub: o.look === 'new' ? o.sub : 'Transition' }));
      default:
        return [];
    }
  }

  /** The story's labels, filtered by what is typed, and the typed label to add when it is new. */
  private labelOptions(
    labels: readonly string[],
    pick: (label: string) => void,
    add: (label: string) => Promise<string | null>,
    display: (label: string) => string = (label) => label,
  ): MenuOption[] {
    const typed = this.query()
      .trim()
      .toUpperCase()
      .replace(/[:.]+$/, '');
    const out = rank(labels, typed, (l) => l).map((l) => option(l, display(l), '', () => pick(l)));
    if (typed && !labels.some((l) => sameName(l, typed))) {
      out.push(
        option(
          `+${typed}`,
          `+ ${display(typed)}`,
          'Add to this story',
          () => void this.addLabel(typed, add, pick),
          {
            look: 'new',
            glyph: '+',
          },
        ),
      );
    }
    return out;
  }

  private async addLabel(
    label: string,
    add: (label: string) => Promise<string | null>,
    pick: (label: string) => void,
  ): Promise<void> {
    const problem = await add(label);
    if (problem) {
      this.store.error.set(problem);
    } else {
      pick(label);
    }
  }

  private textOptions(element: SceneElement): MenuOption[] {
    const menu = this.textMenu();
    if (!menu) {
      return [];
    }
    if (menu.kind === 'slash') {
      return rank(BLOCK_TYPES, menu.query, (t) => t.label).map((t) =>
        option(t.type, t.label, t.hint, () => this.applySlash(element.id, t.type), {
          glyph: t.glyph,
        }),
      );
    }
    const referables = [...this.store.referables()].sort(
      (a, b) => REFERENCE_ORDER[a.kind] - REFERENCE_ORDER[b.kind],
    );
    const out = rank(referables, menu.query, (r) => r.name)
      .slice(0, 6)
      .map((r) => this.referableOption(element.id, r, menu));
    const name = titleCase(menu.query);
    if (name && !referables.some((r) => norm(r.name).startsWith(norm(name)))) {
      out.push(
        option(
          '+character',
          `New character “${name}”`,
          'Adds to the story library',
          () =>
            void this.createReference(
              element.id,
              menu,
              () => this.store.addCharacter(name),
              'character',
            ),
          { look: 'new', glyph: '+' },
        ),
        option(
          '+prop',
          `New prop “${name}”`,
          'Adds to the story library',
          () => void this.createReference(element.id, menu, () => this.store.addProp(name), 'prop'),
          { look: 'new', glyph: '+' },
        ),
      );
    }
    return out;
  }

  private referableOption(id: string, item: Referable, menu: TextMenu): MenuOption {
    const run = () => this.pickReference(id, item, menu);
    const key = `${item.kind}:${item.id}`;
    switch (item.kind) {
      case 'character': {
        const character = this.store.characterById().get(item.id);
        return option(key, item.name, character?.kind ?? 'Character', run, {
          look: 'avatar',
          glyph: initials(item.name),
          hue: character?.hue ?? null,
          shape: character ? this.store.kindOf(character).avatarShape : 'Circle',
        });
      }
      case 'location': {
        const location = this.store.locationById().get(item.id);
        const prefix = location ? settingPrefix(location.setting) : '';
        return option(key, item.name, `Location · ${prefix}`, run, {
          look: 'location',
          glyph: prefix.slice(0, 3),
          hue: location?.hue ?? null,
        });
      }
      case 'prop':
        return option(key, item.name, 'Prop', run, { look: 'prop', glyph: '◆' });
    }
  }

  private pickReference(id: string, item: Referable, menu: TextMenu): void {
    const element = this.find(id);
    if (!element) {
      return;
    }
    const current = textOf(element);
    const typedTo = Math.min(menu.start + 1 + menu.query.length, current.text.length);
    const inserted =
      menu.tagStart !== null
        ? tagReference(current, menu.tagStart, menu.start, item, typedTo)
        : insertReference(current, menu.start, typedTo, item);
    this.textMenu.set(null);
    this.write(id, inserted.value, inserted.caret);
  }

  private async createReference(
    id: string,
    menu: TextMenu,
    create: () => Promise<{ id: string; name: string } | string>,
    kind: 'character' | 'prop',
  ): Promise<void> {
    this.textMenu.set(null);
    const created = await create();
    if (typeof created === 'string') {
      this.store.error.set(created || 'That could not be added.');
      return;
    }
    this.pickReference(id, { kind, id: created.id, name: created.name }, menu);
  }

  private applySlash(id: string, type: SceneElementType): void {
    const element = this.find(id);
    const menu = this.textMenu();
    if (!element || !menu) {
      return;
    }
    const current = textOf(element);
    const value =
      menu.start < 0
        ? current
        : applyTyping(
            current,
            current.text.slice(0, menu.start) +
              current.text.slice(menu.start + 1 + menu.query.length),
            [],
            menu.start,
          );
    this.textMenu.set(null);
    if (value !== current) {
      this.store.setText(id, value);
    }
    const empty = !value.text.trim();
    if (empty || (CONVERTIBLE.includes(type) && CONVERTIBLE.includes(element.type))) {
      this.store.changeType(id, type);
      this.go(id, fieldsOf(type)[0], empty ? 'end' : Math.max(menu.start, 0));
    } else {
      this.insertAfter(id, type);
    }
  }

  private speakerOptions(element: SceneElement): MenuOption[] {
    const query = this.query();
    const { order, likely } = this.speakerOrder(element);
    const lines = this.store.stats().lines;
    const out = rank(order, query, (c) => c.name)
      .slice(0, 7)
      .map((c) =>
        option(
          c.id,
          c.name,
          (c.id === likely && !query ? 'Likely next · ' : '') +
            plural(lines.get(c.id) ?? 0, 'line'),
          () => this.pickSpeaker(element.id, c.id),
          {
            look: 'avatar',
            glyph: initials(c.name),
            hue: c.hue,
            shape: this.store.kindOf(c).avatarShape,
          },
        ),
      );
    const name = titleCase(query);
    if (name && !this.store.characters().some((c) => sameName(c.name, name))) {
      out.push(
        option(
          '+',
          `Add “${name}”`,
          'New character → story library',
          () => void this.addSpeaker(element.id, name),
          {
            look: 'new',
            glyph: '+',
          },
        ),
      );
    }
    return out;
  }

  /** Whoever spoke before the last speaker first, as a conversation alternates; the last one last. */
  private speakerOrder(element: SceneElement): { order: Character[]; likely: string | null } {
    const recent: string[] = [];
    const before = this.store.elements().slice(0, this.index(element.id)).reverse();
    for (const e of before) {
      if (e.type === 'Dialogue' && e.speakerId && !recent.includes(e.speakerId)) {
        recent.push(e.speakerId);
      }
    }
    const byId = this.store.characterById();
    const ids = [
      ...recent.slice(1),
      ...this.store
        .characters()
        .map((c) => c.id)
        .filter((id) => !recent.includes(id)),
      ...recent.slice(0, 1),
    ];
    const order = ids.flatMap((id) => byId.get(id) ?? []);
    return { order, likely: recent.length ? (order[0]?.id ?? null) : null };
  }

  private placeOptions(element: SceneElement): MenuOption[] {
    const query = this.query();
    const parsed = parseLocationQuery(query);
    const setting = parsed.setting ?? this.pendingSetting();
    const locations = [...this.store.locations()].sort(
      (a, b) => +(b.setting === setting) - +(a.setting === setting),
    );
    const out = rank(locations, parsed.name, (l) => l.name)
      .slice(0, 7)
      .map((l) => {
        const prefix = settingPrefix(l.setting);
        return option(
          l.id,
          l.name,
          prefix === 'INT.' ? 'Interior' : 'Exterior',
          () => this.pickPlace(element.id, l.id),
          {
            look: 'location',
            glyph: prefix.slice(0, 3),
            hue: l.hue,
          },
        );
      });
    const name = titleCase(parsed.name);
    if (name && !this.store.locations().some((l) => sameName(l.name, name))) {
      const chosen = setting ?? 'Interior';
      out.push(
        option(
          '+',
          `Add “${settingPrefix(chosen)} ${name}”`,
          'New location → story library',
          () => void this.addPlace(element.id, name, chosen),
          {
            look: 'new',
            glyph: '+',
          },
        ),
      );
    }
    return out;
  }

  // ─── picks ─────────────────────────────────────────────────────────────

  private pickSpeaker(id: string, speakerId: string): void {
    this.store.updateElement(id, { speakerId });
    this.next(id, 'speaker');
  }

  private async addSpeaker(id: string, name: string): Promise<void> {
    const created = await this.store.addCharacter(name);
    if (typeof created === 'string') {
      this.store.error.set(created || 'That could not be added.');
    } else {
      this.pickSpeaker(id, created.id);
    }
  }

  private pickExtension(id: string, extension: string | null): void {
    this.store.updateElement(id, { extension });
    this.next(id, 'ext');
  }

  private pickSetting(id: string, setting: LocationSetting): void {
    const element = this.find(id);
    const location = element && this.locationOf(element);
    if (location) {
      if (location.setting !== setting) {
        void this.store.setLocationSetting(location.id, setting);
      }
    } else {
      this.pendingSetting.set(setting);
    }
    this.next(id, 'setting');
  }

  private pickPlace(id: string, locationId: string): void {
    this.store.updateElement(id, { locationId });
    this.next(id, 'place');
  }

  private async addPlace(id: string, name: string, setting: LocationSetting): Promise<void> {
    const created = await this.store.addLocation(name, setting);
    if (typeof created === 'string') {
      this.store.error.set(created || 'That could not be added.');
    } else {
      this.pickPlace(id, created.id);
    }
  }

  private pickTime(id: string, timeOfDay: string): void {
    this.store.updateElement(id, { timeOfDay });
    this.next(id, 'time');
  }

  private pickTransition(id: string, transition: string): void {
    this.store.updateElement(id, { transition });
    this.next(id, 'trans');
  }

  // ─── helpers ───────────────────────────────────────────────────────────

  private find(id: string): SceneElement | null {
    return this.store.elements().find((e) => e.id === id) ?? null;
  }

  private index(id: string): number {
    return this.store.elements().findIndex((e) => e.id === id);
  }

  speakerOf(element: SceneElement) {
    return element.speakerId ? (this.store.characterById().get(element.speakerId) ?? null) : null;
  }

  locationOf(element: SceneElement) {
    return element.locationId ? (this.store.locationById().get(element.locationId) ?? null) : null;
  }

  /** The chip or list item a picker opens on: the block's current value. */
  private currentIndex(element: SceneElement, field: BlockField): number {
    let at = -1;
    switch (field) {
      case 'ext':
        at = element.extension ? this.store.extensions().indexOf(element.extension) + 1 : 0;
        break;
      case 'setting':
        at = (this.locationOf(element)?.setting ?? this.pendingSetting()) === 'Exterior' ? 1 : 0;
        break;
      case 'time':
        at = this.store.timesOfDay().indexOf(element.timeOfDay ?? '');
        break;
      case 'trans':
        at = this.store.transitions().indexOf(element.transition ?? '');
        break;
    }
    return Math.max(0, at);
  }

  private setTextMenu(menu: TextMenu | null): void {
    if (menu && menu.start === this.dismissed) {
      menu = null;
    }
    if (!sameMenu(this.textMenu(), menu)) {
      this.textMenu.set(menu);
      this.highlight.set(0);
    }
  }

  private findSlash(text: string, caret: number): TextMenu | null {
    const match = SLASH_QUERY.exec(text.slice(0, caret));
    if (!match) {
      return null;
    }
    return { kind: 'slash', start: caret - match[2].length - 1, query: match[2], tagStart: null };
  }

  /** Closes once nothing matches and the writer is past a word, so "@Mara, " stops asking. */
  private findMention(text: string, caret: number, previous: TextMenu | null): TextMenu | null {
    const match = MENTION_QUERY.exec(text.slice(0, caret));
    if (!match) {
      return null;
    }
    const query = match[1];
    const start = caret - query.length - 1;
    const before = text[start - 1];
    let tagStart: number | null = null;
    if (previous?.kind === 'mention' && previous.start === start) {
      tagStart = previous.tagStart;
    } else if (before !== undefined && WORD_CHAR.test(before)) {
      tagStart = wordStart(text, start);
    } else if (before !== undefined && !MENTION_LEAD.test(before)) {
      return null;
    }
    const needle = query.toLowerCase();
    const matches = this.store.referables().some((r) => r.name.toLowerCase().includes(needle));
    if (!matches && (/\s/.test(query) || ENDS_OFF_WORD.test(query))) {
      return null;
    }
    return { kind: 'mention', start, query, tagStart };
  }

  /** `him@Mara` followed by a boundary tags "him" and drops the `@Mara`, as a typed `@Mara` links. */
  private linkTypedTag(value: MentionedText, menu: TextMenu): ReferenceInsertion | null {
    const { start, tagStart } = menu;
    if (tagStart === null || tagStart >= start || value.text[start] !== '@') {
      return null;
    }
    const match = matchName(value.text, start + 1, this.store.referables());
    if (!match) {
      return null;
    }
    return tagReference(value, tagStart, start, match, start + 1 + match.name.length);
  }

  /** Mirrored into the field at once: keys queued before the next render would read the old text. */
  private write(id: string, value: MentionedText, caret: number): void {
    this.store.setText(id, value);
    this.store.caret.set(caret);
    this.store.selectionEnd.set(caret);
    const field = this.page?.querySelector<HTMLTextAreaElement>(`[data-f="${id}:text"]`);
    if (field) {
      field.value = value.text;
      field.focus({ preventScroll: true });
      field.setSelectionRange(caret, caret);
    }
    this.focusRequest.set({ id, field: 'text', caret });
  }
}
