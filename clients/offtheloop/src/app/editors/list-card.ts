import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { joinWants, splitWants } from '../loop/wants';
import { Icon } from '../shared/icon';
import { ListField, ListMarker } from './list-field';
import { SideSheet } from './side-sheet';

/** A list as a card: the first few entries, then the whole list in a sheet to read and edit. */
@Component({
  selector: 'app-list-card',
  imports: [FormsModule, Icon, ListField, SideSheet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'card card-pad', role: 'region', '[attr.aria-label]': 'heading()' },
  template: `
    <div class="card-head">
      <h3 class="card-title">
        {{ heading() }}
        @if (items().length) {
          <span class="badge">{{ items().length }}</span>
        }
      </h3>
      @if (items().length) {
        <button type="button" class="btn btn-xs" (click)="open('read')">
          <app-icon name="edit" [size]="13" />Edit
        </button>
      }
    </div>

    @if (!items().length) {
      <p class="quiet">{{ empty() }}</p>
    } @else if (split()) {
      <div class="preview-columns">
        @for (group of groups(); track group.label) {
          <div class="flex min-w-0 flex-col gap-2">
            <h4 class="field-label">{{ group.label }} · {{ group.items.length }}</h4>
            @if (group.items.length) {
              <ul class="preview">
                @for (item of group.items.slice(0, limit()); track $index) {
                  <li>
                    <span class="list-marker" data-marker="·" aria-hidden="true"></span
                    ><span class="min-w-0">{{ item }}</span>
                  </li>
                }
              </ul>
              @if (group.items.length > limit()) {
                <p class="more-line">and {{ group.items.length - limit() }} more</p>
              }
            } @else {
              <p class="quiet">None marked.</p>
            }
          </div>
        }
      </div>
    } @else if (look() === 'tags') {
      <ul class="tags m-0 list-none p-0">
        @for (item of items().slice(0, tagLimit); track $index) {
          <li class="tag-flat" [class.mono]="mono()">{{ item }}</li>
        }
      </ul>
    } @else {
      <ul class="preview">
        @for (item of items().slice(0, limit()); track $index) {
          <li>
            <span class="list-marker" [attr.data-marker]="marker()" aria-hidden="true">{{
              marker() === '·' ? '' : marker()
            }}</span
            ><span class="min-w-0">{{ item }}</span>
          </li>
        }
      </ul>
    }

    <div class="card-actions">
      @if (hidden()) {
        <button type="button" class="btn btn-xs" (click)="open('read')">
          Show all {{ items().length }}<app-icon name="next" [size]="13" />
        </button>
      }
      <button type="button" class="btn-add" (click)="open('add')">
        <app-icon name="plus" [size]="11" [weight]="2" />Add
      </button>
    </div>

    @if (sheet(); as mode) {
      <app-side-sheet
        [heading]="heading()"
        [count]="items().length"
        [subtitle]="subtitle()"
        (closed)="sheet.set(null)"
      >
        <label sheet-tools class="search-box">
          <app-icon name="search" [size]="15" />
          <span class="sr-only">Find in {{ heading() }}</span>
          <input
            type="search"
            name="query"
            autocomplete="off"
            [attr.autofocus]="mode === 'read' ? '' : null"
            [placeholder]="'Find in ' + items().length"
            [(ngModel)]="query"
          />
        </label>
        <p class="hint">Click an entry to change it; clear it to remove it.</p>
        @if (split()) {
          @for (group of groups(); track group.label; let first = $first) {
            <div class="flex flex-col gap-1">
              <h3 class="field-label mb-1">{{ group.label }}</h3>
              <app-list-field
                [label]="group.label"
                [heading]="false"
                [items]="group.items"
                [placeholder]="placeholder()"
                [query]="query()"
                [focusAdd]="first && mode === 'add'"
                [save]="first ? saveMust : saveNice"
              />
            </div>
          }
        } @else {
          <app-list-field
            [label]="heading()"
            [heading]="false"
            [items]="items()"
            [placeholder]="placeholder()"
            [look]="look()"
            [marker]="marker()"
            [query]="query()"
            [focusAdd]="mode === 'add'"
            [save]="save()"
          />
        }
      </app-side-sheet>
    }
  `,
})
export class ListCard {
  readonly heading = input.required<string>();
  readonly items = input.required<readonly string[]>();
  readonly save = input.required<(items: string[]) => Promise<unknown>>();
  readonly placeholder = input('');
  readonly look = input<'rows' | 'tags'>('rows');
  readonly marker = input<ListMarker>('·');
  readonly mono = input(false, { transform: booleanAttribute });
  /** Must-haves apart from the nice-to-haves marked "(bonus)". */
  readonly split = input(false, { transform: booleanAttribute });
  readonly empty = input('Nothing yet.');
  readonly subtitle = input('');
  readonly limit = input(5);

  protected readonly tagLimit = 12;
  protected readonly sheet = signal<'read' | 'add' | null>(null);
  protected readonly query = signal('');

  protected readonly groups = computed(() => {
    const { must, nice } = splitWants(this.items());
    return [
      { label: 'Must have', items: must },
      { label: 'Nice to have', items: nice },
    ];
  });

  protected readonly hidden = computed(() => {
    if (this.split()) {
      return this.groups().some((group) => group.items.length > this.limit());
    }
    return this.items().length > (this.look() === 'tags' ? this.tagLimit : this.limit());
  });

  protected readonly saveMust = (must: string[]) =>
    this.save()(joinWants(must, this.groups()[1].items));
  protected readonly saveNice = (nice: string[]) =>
    this.save()(joinWants(this.groups()[0].items, nice));

  protected open(mode: 'read' | 'add'): void {
    this.query.set('');
    this.sheet.set(mode);
  }
}
