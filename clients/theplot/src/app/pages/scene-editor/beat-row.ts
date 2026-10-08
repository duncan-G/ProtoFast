import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import {
  applyTyping,
  insertReference,
  linkTypedReferences,
  matchName,
  REFERENCE_ORDER,
  tagReference,
  wordStart,
} from '../../stories/mentions';
import { MentionedText } from '../../stories/model/mentioned-text';
import { Referable } from '../../stories/model/referable';
import { ReferenceInsertion } from '../../stories/model/reference-insertion';
import { SceneElementType } from '../../stories/model/scene-element-type';
import { Avatar } from './avatar';
import { initials, settingPrefix } from './format';
import { LabelChips } from './label-chips';
import { MentionText } from './mention-text';
import { ReferenceQuery } from './reference-query';
import { RowActions } from './row-actions';
import { SceneEditorStore } from './scene-editor-store';
import { SceneRow } from './scene-row';
import { SpeakerPicker } from './speaker-picker';

const BEAT_TYPES: { type: SceneElementType; label: string }[] = [
  { type: 'Action', label: 'Action' },
  { type: 'Description', label: 'Description' },
  { type: 'Narration', label: 'Narration' },
  { type: 'Dialogue', label: 'Dialogue' },
];

const PLACEHOLDERS: Partial<Record<SceneElementType, string>> = {
  Action: 'What happens? e.g. @Mara ducks behind the car…',
  Description: 'What does it look, sound, feel like?',
  Narration: 'The narrator’s voice…',
  Dialogue: 'What do they say?',
};

/** An `@` and up to 24 characters of name typed since. */
const QUERY = /@([^@\n]{0,24})$/;
/** Before an `@`, these start a mention; a word character instead makes it a tag (`him@`). */
const MENTION_LEAD = /[\s(“"‘']/;
const WORD_CHAR = /[\p{L}\p{N}]/u;
const ENDS_OFF_WORD = /[^\p{L}\p{N}]$/u;

/** A picked reference links to the pick, so a name two entries share goes to the one chosen. */
@Component({
  selector: 'app-beat-row',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, LabelChips, MentionText, RowActions, SpeakerPicker],
  templateUrl: './beat-row.html',
})
export class BeatRow {
  protected readonly store = inject(SceneEditorStore);

  readonly row = input.required<SceneRow>();
  readonly editing = input(false);

  protected readonly types = BEAT_TYPES;
  protected readonly query = signal<ReferenceQuery | null>(null);
  protected readonly active = signal(0);
  protected readonly selecting = signal(false);
  private readonly textarea = viewChild<ElementRef<HTMLTextAreaElement>>('textarea');
  private readonly backdrop = viewChild<ElementRef<HTMLElement>>('backdrop');
  private focused: HTMLTextAreaElement | null = null;

  protected readonly element = computed(() => this.row().element);
  protected readonly isDialogue = computed(() => this.element().type === 'Dialogue');
  protected readonly typeLabel = computed(
    () => BEAT_TYPES.find((t) => t.type === this.element().type)?.label ?? '',
  );
  protected readonly placeholder = computed(() => PLACEHOLDERS[this.element().type] ?? '');
  protected readonly speaker = computed(() => {
    const id = this.element().speakerId;
    return id ? (this.store.characterById().get(id) ?? null) : null;
  });
  protected readonly speakerInitials = computed(() => initials(this.speaker()?.name ?? ''));

  protected readonly tagging = computed(() => {
    const query = this.query();
    if (query?.tagStart == null) {
      return null;
    }
    const text = this.element().text ?? '';
    return text.slice(query.tagStart, query.start).trim();
  });

  protected readonly suggestions = computed(() => {
    const query = this.query();
    if (!query) {
      return [];
    }
    return this.matching(query.query).sort(
      (a, b) => REFERENCE_ORDER[a.kind] - REFERENCE_ORDER[b.kind] || a.name.localeCompare(b.name),
    );
  });

  constructor() {
    afterRenderEffect(() => {
      const textarea = this.textarea()?.nativeElement ?? null;
      if (textarea && textarea !== this.focused) {
        textarea.focus({ preventScroll: true });
        textarea.setSelectionRange(textarea.value.length, textarea.value.length);
        this.store.caret.set(textarea.value.length);
        this.store.selectionEnd.set(textarea.value.length);
      }
      this.focused = textarea;
    });
    afterRenderEffect(() => {
      const request = this.store.caretRequest();
      const textarea = this.textarea()?.nativeElement;
      if (request && textarea && request.elementId === this.element().id) {
        textarea.focus({ preventScroll: true });
        textarea.setSelectionRange(request.caret, request.caret);
        this.store.caretRequest.set(null);
      }
    });
  }

  protected edit(): void {
    this.store.editingId.set(this.element().id);
  }

  protected finish(): void {
    this.closeQuery();
    this.store.editingId.set(null);
  }

  protected readonly addExtension = (label: string) => this.store.addExtension(label);

  /** Picking the current extension clears it. */
  protected toggleExtension(label: string): void {
    const extension = label === this.element().extension ? null : label;
    this.store.updateElement(this.element().id, { extension });
  }

  protected setParenthetical(value: string): void {
    this.store.updateElement(this.element().id, { parenthetical: value });
  }

  /** Stored without its parentheses; blank is null. */
  protected commitParenthetical(): void {
    const raw = this.element().parenthetical ?? '';
    const bare = raw
      .trim()
      .replace(/^\(\s*/, '')
      .replace(/\s*\)$/, '');
    this.store.updateElement(this.element().id, { parenthetical: bare || null });
  }

  protected kindLabel(item: Referable): string {
    switch (item.kind) {
      case 'character':
        return this.store.characterById().get(item.id)?.kind ?? 'Character';
      case 'location': {
        const location = this.store.locationById().get(item.id);
        return location ? `Location · ${settingPrefix(location.setting)}` : 'Location';
      }
      case 'prop':
        return 'Prop';
    }
  }

  protected hueOf(item: Referable): number | null {
    if (item.kind === 'character') {
      return this.store.characterById().get(item.id)?.hue ?? null;
    }
    return item.kind === 'location' ? (this.store.locationById().get(item.id)?.hue ?? null) : null;
  }

  protected shapeOf(item: Referable) {
    const character = this.store.characterById().get(item.id);
    return character ? this.store.kindOf(character).avatarShape : 'Circle';
  }

  protected initialsOf(item: Referable): string {
    return initials(item.name);
  }

  protected settingOf(item: Referable): string {
    const location = this.store.locationById().get(item.id);
    return location ? settingPrefix(location.setting).slice(0, 3) : '';
  }

  // ─── the text ──────────────────────────────────────────────────────────

  private current(): MentionedText {
    return { text: this.element().text ?? '', mentions: this.element().mentions };
  }

  private matching(query: string): Referable[] {
    const needle = query.toLowerCase();
    return this.store.referables().filter((r) => r.name.toLowerCase().includes(needle));
  }

  /** Closes once nothing matches and the writer is past a word, so "@Mara, " stops asking. */
  private findQuery(text: string, caret: number): ReferenceQuery | null {
    const match = QUERY.exec(text.slice(0, caret));
    if (!match) {
      return null;
    }
    const query = match[1];
    const start = caret - query.length - 1;
    const before = text[start - 1];
    const previous = this.query();
    let tagStart: number | null = null;
    if (previous?.start === start) {
      tagStart = previous.tagStart;
    } else if (before !== undefined && WORD_CHAR.test(before)) {
      tagStart = wordStart(text, start);
    } else if (before !== undefined && !MENTION_LEAD.test(before)) {
      return null;
    }
    if (this.matching(query).length === 0 && (/\s/.test(query) || ENDS_OFF_WORD.test(query))) {
      return null;
    }
    return { start, query, tagStart };
  }

  /** `him@Mara` followed by a boundary tags "him" and drops the `@Mara`, as a typed `@Mara` links. */
  private linkTypedTag(value: MentionedText, query: ReferenceQuery): ReferenceInsertion | null {
    const { start, tagStart } = query;
    if (tagStart === null || tagStart >= start || value.text[start] !== '@') {
      return null;
    }
    const match = matchName(value.text, start + 1, this.store.referables());
    if (!match) {
      return null;
    }
    return tagReference(value, tagStart, start, match, start + 1 + match.name.length);
  }

  protected onInput(): void {
    const textarea = this.textarea()!.nativeElement;
    const caret = textarea.selectionStart;
    const previous = this.query();
    const candidate = this.findQuery(textarea.value, caret);
    const referables = this.store.referables();
    let value = applyTyping(
      this.current(),
      textarea.value,
      referables,
      caret,
      candidate?.start ?? null,
    );
    const query =
      candidate && !value.mentions.some((m) => m.offset === candidate.start) ? candidate : null;
    this.query.set(query);
    this.active.set(0);
    if (previous && previous.start !== query?.start) {
      if (previous.tagStart === null) {
        value = linkTypedReferences(value, referables, previous.start, previous.start);
      } else if (caret > previous.start) {
        const tagged = this.linkTypedTag(value, previous);
        if (tagged) {
          const removed = value.text.length - tagged.value.text.length;
          this.write(
            tagged.value,
            caret >= tagged.caret + removed ? caret - removed : tagged.caret,
          );
          return;
        }
      }
    }
    this.store.setText(this.element().id, value);
    this.trackCaret();
  }

  protected closeQuery(): void {
    const query = this.query();
    if (!query) {
      return;
    }
    this.query.set(null);
    const current = this.current();
    if (query.tagStart !== null) {
      const tagged = this.linkTypedTag(current, query);
      if (tagged) {
        this.write(tagged.value, tagged.caret);
      }
      return;
    }
    const value = linkTypedReferences(current, this.store.referables(), query.start, query.start);
    if (value !== current) {
      this.store.setText(this.element().id, value);
    }
  }

  protected pickReference(item: Referable): void {
    const textarea = this.textarea()?.nativeElement;
    const caret = textarea?.selectionStart ?? this.current().text.length;
    const query = this.query();
    const start = query?.start ?? caret;
    const inserted =
      query?.tagStart != null
        ? tagReference(this.current(), query.tagStart, start, item, caret)
        : insertReference(this.current(), start, caret, item);
    this.query.set(null);
    this.write(inserted.value, inserted.caret);
  }

  protected openReferenceSearch(): void {
    if (this.tagSelection()) {
      return;
    }
    const textarea = this.textarea()?.nativeElement;
    const current = this.current();
    const caret = textarea?.selectionStart ?? current.text.length;
    const lead = caret > 0 && !/\s/.test(current.text[caret - 1]) ? ' ' : '';
    const text = current.text.slice(0, caret) + lead + '@' + current.text.slice(caret);
    const at = caret + lead.length + 1;
    this.write(applyTyping(current, text, [], at), at);
    this.query.set({ start: at - 1, query: '', tagStart: null });
    this.active.set(0);
  }

  /** An `@` typed over a selection tags it: the `@` goes after the selection to search from. */
  private tagSelection(): boolean {
    const textarea = this.textarea()?.nativeElement;
    if (!textarea || textarea.selectionEnd <= textarea.selectionStart) {
      return false;
    }
    const { selectionStart: from, selectionEnd: to } = textarea;
    const current = this.current();
    const text = current.text.slice(0, to) + '@' + current.text.slice(to);
    this.write(applyTyping(current, text, [], to + 1), to + 1);
    this.query.set({ start: to, query: '', tagStart: from });
    this.active.set(0);
    return true;
  }

  /** Mirrored into the field at once: keys queued before the next render would read the old text. */
  private write(value: MentionedText, caret: number): void {
    this.store.setText(this.element().id, value);
    this.store.caret.set(caret);
    this.store.selectionEnd.set(caret);
    this.selecting.set(false);
    const textarea = this.textarea()?.nativeElement;
    if (textarea) {
      textarea.value = value.text;
      textarea.focus({ preventScroll: true });
      textarea.setSelectionRange(caret, caret);
    }
  }

  protected trackCaret(): void {
    const textarea = this.textarea()?.nativeElement;
    if (textarea) {
      this.store.caret.set(textarea.selectionStart);
      this.store.selectionEnd.set(textarea.selectionEnd);
      this.selecting.set(textarea.selectionEnd > textarea.selectionStart);
    }
  }

  protected syncScroll(): void {
    const textarea = this.textarea()?.nativeElement;
    const backdrop = this.backdrop()?.nativeElement;
    if (textarea && backdrop) {
      backdrop.scrollTop = textarea.scrollTop;
    }
  }

  protected onKey(event: KeyboardEvent): void {
    if (event.key === '@' && !event.ctrlKey && !event.metaKey && this.tagSelection()) {
      event.preventDefault();
      return;
    }
    const suggestions = this.suggestions();
    if (this.query() && suggestions.length) {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        const step = event.key === 'ArrowDown' ? 1 : -1;
        this.active.update((i) => (i + step + suggestions.length) % suggestions.length);
        return;
      }
      if (event.key === 'Enter' || event.key === 'Tab') {
        event.preventDefault();
        this.pickReference(suggestions[this.active()]);
        return;
      }
    }
    if (event.altKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
      event.preventDefault();
      const caret = this.textarea()?.nativeElement.selectionStart ?? 0;
      this.store.moveElement(this.element().id, event.key === 'ArrowUp' ? -1 : 1);
      this.store.caretRequest.set({ elementId: this.element().id, caret });
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      if (this.query()) {
        this.closeQuery();
      } else {
        this.finish();
      }
      return;
    }
    if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
      event.preventDefault();
      this.finish();
    }
  }
}
