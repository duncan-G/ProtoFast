import {
  afterNextRender,
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  input,
  OnInit,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { messageOf } from '../loop/errors';
import { Icon } from '../shared/icon';
import { DateField } from './date-field';

type Draft = Record<string, unknown>;

/** Fixed, or following what the form holds so far. */
export type FieldText = string | ((draft: Draft) => string);

export interface FieldSpec {
  key: string;
  label: FieldText;
  hint?: FieldText;
  placeholder?: string;
  type?: 'text' | 'date' | 'url' | 'textarea' | 'choice';
  /** A choice's options; picking the chosen one again clears it, unless required or one is null. */
  options?: readonly { value: unknown; label: string }[];
  /** A date's quick picks; without them it is one button. */
  picks?: 'past' | 'ahead';
  /** A date pick that clears it. */
  none?: string;
  look?: 'lead' | 'large' | 'mono' | 'figure';
  /** Takes the whole row; choices and textareas always do. */
  wide?: boolean;
  required?: boolean;
}

let nextId = 0;

/** A section's fields in a panel where they're shown, or a sheet when adding. Blank dates save null. */
@Component({
  selector: 'app-fields-form',
  imports: [DateField, FormsModule, Icon, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    @if (sheet()) {
      <dialog
        #dialog
        class="sheet"
        [attr.aria-labelledby]="id + '-heading'"
        (close)="closed.emit()"
        (click)="closeOnBackdrop($event)"
      >
        <ng-container *ngTemplateOutlet="form" />
      </dialog>
    } @else {
      <ng-container *ngTemplateOutlet="form" />
    }

    <ng-template #form>
      <form
        class="edit-form"
        [class.edit-panel]="!sheet()"
        [attr.aria-labelledby]="id + '-heading'"
        (ngSubmit)="submit()"
        (keydown)="onKeydown($event)"
      >
        <div class="edit-head">
          @if (sheet()) {
            <h2 class="edit-heading" [id]="id + '-heading'">{{ heading() }}</h2>
            <button
              type="button"
              class="btn btn-icon -my-2 -mr-2 border-0"
              aria-label="Close"
              (click)="cancel()"
            >
              <app-icon name="close" [size]="15" />
            </button>
          } @else {
            <h3 class="edit-heading" [id]="id + '-heading'">editing · {{ heading() }}</h3>
            <span class="keys wide-only" aria-hidden="true">
              <span><kbd>esc</kbd> cancel</span>
              <span><kbd>{{ mod() }}↵</kbd> save</span>
            </span>
          }
        </div>

        <div class="edit-body">
          <div class="form-grid" [class.figures]="layout() === 'figures'">
            @for (field of fields(); track field.key) {
              @let fid = id + '-' + field.key;
              <div
                class="field"
                [class.wide]="field.wide || field.type === 'textarea' || field.type === 'choice'"
              >
                @if (field.look !== 'figure') {
                  <div class="field-head">
                    @if (field.type === 'choice' || field.type === 'date') {
                      <span [id]="fid + '-label'">{{ text(field.label) }}</span>
                    } @else {
                      <label [for]="fid">{{ text(field.label) }}</label>
                    }
                    @if (field.hint && text(field.hint)) {
                      <span class="field-hint">{{ text(field.hint) }}</span>
                    }
                  </div>
                }
                @switch (field.type) {
                  @case ('textarea') {
                    <textarea
                      class="input input-box"
                      rows="4"
                      [class.mono]="field.look === 'mono'"
                      [id]="fid"
                      [name]="field.key"
                      [placeholder]="field.placeholder ?? ''"
                      [(ngModel)]="draft[field.key]"
                    ></textarea>
                  }
                  @case ('choice') {
                    <div role="radiogroup" class="picks" [attr.aria-labelledby]="fid + '-label'">
                      @for (option of field.options; track option.label) {
                        <button
                          type="button"
                          role="radio"
                          class="pick"
                          [attr.aria-checked]="draft[field.key] === option.value"
                          (click)="choose(field, option.value)"
                        >
                          {{ option.label }}
                        </button>
                      }
                    </div>
                  }
                  @case ('date') {
                    <app-date-field
                      [label]="text(field.label)"
                      [labelledBy]="fid + '-label'"
                      [picks]="field.picks ?? null"
                      [none]="field.none ?? ''"
                      [hot]="field.picks === 'ahead'"
                      [value]="dayOf(field)"
                      (valueChange)="draft[field.key] = $event"
                    />
                  }
                  @default {
                    <input
                      class="input"
                      autocomplete="off"
                      [class.input-lead]="field.look === 'lead'"
                      [class.input-large]="field.look === 'large'"
                      [class.input-figure]="field.look === 'figure'"
                      [class.mono]="field.look === 'mono'"
                      [type]="field.type ?? 'text'"
                      [id]="fid"
                      [name]="field.key"
                      [placeholder]="field.placeholder ?? ''"
                      [required]="!!field.required"
                      [(ngModel)]="draft[field.key]"
                    />
                  }
                }
                @if (field.look === 'figure') {
                  <label class="figure-label" [for]="fid">{{ text(field.label) }}</label>
                }
              </div>
            }
          </div>
          @if (error()) {
            <p class="alert" role="alert">{{ error() }}</p>
          }
        </div>

        <div class="edit-foot">
          @if (note()) {
            <span class="edit-note wide-only">{{ note() }}</span>
          }
          <button type="button" class="btn btn-sm" (click)="cancel()">Cancel</button>
          <button type="submit" class="btn btn-sm btn-primary" [disabled]="busy()">
            @if (busy()) {
              <span class="spinner" aria-hidden="true"></span>
            }
            {{ saveLabel() }}
            @if (sheet()) {
              <span class="keys-inline wide-only" aria-hidden="true">{{ mod() }}↵</span>
            }
          </button>
        </div>
      </form>
    </ng-template>
  `,
})
export class FieldsForm implements OnInit {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('dialog');

  readonly fields = input.required<readonly FieldSpec[]>();
  readonly value = input.required<object>();
  readonly save = input.required<(changes: Record<string, unknown>) => Promise<unknown>>();
  /** What is being edited, or the sheet's title. */
  readonly heading = input.required<string>();
  readonly layout = input<'pairs' | 'figures'>('pairs');
  /** Over the page, for adding, rather than in place of what it edits. */
  readonly sheet = input(false, { transform: booleanAttribute });
  /** A line beside the buttons. */
  readonly note = input('');
  readonly saveLabel = input('Save');
  readonly failure = input('That could not be saved.');
  /** After a save, or on cancel. */
  readonly closed = output<void>();

  protected readonly id = `fields-${nextId++}`;
  protected readonly mod = signal('⌘');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected draft: Draft = {};

  constructor() {
    afterNextRender(() => {
      this.dialog()?.nativeElement.showModal();
      if (!/Mac|iPhone|iPad/.test(navigator.userAgent)) {
        this.mod.set('ctrl ');
      }
      this.host.nativeElement
        .querySelector<HTMLElement>('.edit-body input:not(.date-native), .edit-body textarea')
        ?.focus();
    });
  }

  ngOnInit(): void {
    const value = this.value() as Draft;
    for (const field of this.fields()) {
      const current = value[field.key];
      this.draft[field.key] = field.type === 'date' ? (current ?? '') : current;
    }
  }

  protected text(text: FieldText): string {
    return typeof text === 'function' ? text(this.draft) : text;
  }

  protected dayOf(field: FieldSpec): string {
    return String(this.draft[field.key] ?? '');
  }

  protected choose(field: FieldSpec, value: unknown): void {
    const keeps = field.required || field.options?.some((option) => option.value === null);
    this.draft[field.key] = this.draft[field.key] === value && !keeps ? null : value;
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
      event.preventDefault();
      void this.submit();
    } else if (event.key === 'Escape' && !this.sheet()) {
      event.preventDefault();
      this.closed.emit();
    }
  }

  protected cancel(): void {
    const dialog = this.dialog();
    if (dialog) {
      dialog.nativeElement.close();
    } else {
      this.closed.emit();
    }
  }

  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.dialog()?.nativeElement) {
      this.cancel();
    }
  }

  protected async submit(): Promise<void> {
    const blank = this.fields().find(
      (field) => field.required && !String(this.draft[field.key] ?? '').trim(),
    );
    if (blank) {
      this.error.set(`Fill in ${this.text(blank.label)}.`);
      return;
    }
    if (this.busy()) {
      return;
    }
    const changes: Record<string, unknown> = {};
    for (const field of this.fields()) {
      const value = this.draft[field.key];
      changes[field.key] = field.type === 'date' && !value ? null : value;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.save()(changes);
      this.cancel();
    } catch (err) {
      this.error.set(messageOf(err, this.failure()));
    } finally {
      this.busy.set(false);
    }
  }
}
