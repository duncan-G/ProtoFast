import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  Injector,
  input,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DateField } from '../../editors/date-field';
import { shortDate, today } from '../../loop/days';
import { messageOf } from '../../loop/errors';
import { LoopStore } from '../../loop/loop-store';
import { initials } from '../../loop/loop-summary';
import { Company } from '../../loop/model/company';
import { Icon } from '../../shared/icon';

/** The people at a company, each changed or added in place. */
@Component({
  selector: 'app-people',
  imports: [DateField, FormsModule, Icon, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'card card-pad dim-scope gap-0', role: 'region', 'aria-label': 'People' },
  template: `
    <div class="card-head mb-1">
      <h3 class="card-title">
        People
        @if (company().contacts.length) {
          <span class="badge">{{ company().contacts.length }}</span>
        }
      </h3>
    </div>
    @for (person of company().contacts; track $index; let i = $index) {
      @if (editing() === i) {
        <ng-container *ngTemplateOutlet="form" />
      } @else {
        <div class="flex items-start gap-3 border-b border-[var(--color-line)] py-2.5">
          <span
            class="flex h-9 w-9 flex-none items-center justify-center rounded-full bg-[var(--color-line)] font-mono text-[12px] font-medium text-[var(--color-text-2)]"
            aria-hidden="true"
            >{{ initials(person.name) }}</span
          >
          <span class="flex min-w-0 flex-1 flex-col gap-px">
            <span class="flex items-baseline justify-between gap-2">
              <span class="truncate text-[14.5px] font-medium text-[var(--color-text)]">{{
                person.name
              }}</span>
              @if (person.metOn) {
                <span class="meta text-[11.5px]">{{ shortDate(person.metOn) }}</span>
              }
            </span>
            @if (person.role) {
              <span class="text-[12.5px] text-[var(--color-dim)]">{{ person.role }}</span>
            }
            @if (person.note) {
              <span class="mt-0.5 text-[13.5px] text-[var(--color-muted)]">{{ person.note }}</span>
            }
          </span>
          <button
            type="button"
            class="btn btn-icon btn-xs h-8 min-h-8 w-8 flex-none"
            [attr.aria-label]="'Edit ' + person.name"
            (click)="edit(i)"
          >
            <app-icon name="edit" [size]="13" />
          </button>
        </div>
      }
    } @empty {
      @if (editing() !== 'new') {
        <p class="quiet mb-2">
          Nobody yet. Add the recruiter, the hiring manager, who referred you.
        </p>
      }
    }
    @if (editing() === 'new') {
      <ng-container *ngTemplateOutlet="form" />
    } @else {
      <button type="button" class="btn-add mt-3 self-start" (click)="edit('new')">
        <app-icon name="plus" [size]="11" [weight]="2" />Add person
      </button>
    }

    <ng-template #form>
      <form
        class="edit-form edit-panel my-2"
        aria-labelledby="person-heading"
        (ngSubmit)="save()"
        (keydown)="onKeydown($event)"
      >
        <div class="edit-head">
          <h4 id="person-heading" class="edit-heading">
            {{ editing() === 'new' ? 'new person · ' + company().name : 'editing · ' + heading }}
          </h4>
        </div>
        <div class="edit-body">
          <div class="flex items-end gap-4">
            <span
              class="mb-0.5 flex h-10 w-10 flex-none items-center justify-center rounded-full bg-[var(--color-line)] text-[13.5px] font-semibold text-[var(--color-muted)]"
              aria-hidden="true"
              >{{ initials(person.name) || '?' }}</span
            >
            <div class="form-grid flex-1">
              <div class="field">
                <div class="field-head"><label for="person-name">name</label></div>
                <input
                  id="person-name"
                  class="input font-medium"
                  name="name"
                  required
                  autocomplete="off"
                  [(ngModel)]="person.name"
                />
              </div>
              <div class="field">
                <div class="field-head"><label for="person-role">role</label></div>
                <input
                  id="person-role"
                  class="input mono"
                  name="role"
                  placeholder="recruiter"
                  autocomplete="off"
                  [(ngModel)]="person.role"
                />
              </div>
            </div>
          </div>
          <div class="field">
            <div class="field-head"><label for="person-note">note</label></div>
            <input
              id="person-note"
              class="input"
              name="note"
              placeholder="what they care about, what they asked"
              autocomplete="off"
              [(ngModel)]="person.note"
            />
          </div>
          <div class="field">
            <div class="field-head"><span id="person-met">when you spoke</span></div>
            <app-date-field
              label="when you spoke"
              labelledBy="person-met"
              picks="past"
              none="not yet"
              [(value)]="person.metOn"
            />
          </div>
          @if (error()) {
            <p class="alert" role="alert">{{ error() }}</p>
          }
        </div>
        <div class="edit-foot">
          @if (editing() !== 'new') {
            <button
              type="button"
              class="btn btn-sm btn-danger mr-auto"
              [disabled]="busy()"
              (click)="remove()"
            >
              Remove
            </button>
          }
          <button type="button" class="btn btn-sm" (click)="stop()">Cancel</button>
          <button
            type="submit"
            class="btn btn-sm btn-primary"
            [disabled]="!person.name.trim() || busy()"
          >
            {{ editing() === 'new' ? 'Add person' : 'Save' }}
          </button>
        </div>
      </form>
    </ng-template>
  `,
})
export class People {
  private readonly store = inject(LoopStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  readonly company = input.required<Company>();

  protected readonly initials = initials;
  protected readonly shortDate = shortDate;
  protected readonly editing = signal<number | 'new' | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected person = { name: '', role: '', note: '', metOn: '' };
  /** The name as it was when the form opened. */
  protected heading = '';

  protected edit(at: number | 'new'): void {
    const current = at === 'new' ? null : this.company().contacts[at];
    this.person = current
      ? { ...current, metOn: current.metOn ?? '' }
      : { name: '', role: '', note: '', metOn: today() };
    this.heading = current?.name ?? '';
    this.error.set('');
    this.editing.set(at);
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLElement>('form input')?.focus(),
      {
        injector: this.injector,
      },
    );
  }

  protected stop(): void {
    this.editing.set(null);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
      event.preventDefault();
      void this.save();
    } else if (event.key === 'Escape') {
      event.preventDefault();
      this.stop();
    }
  }

  protected save(): Promise<void> {
    const at = this.editing();
    if (at === null || !this.person.name.trim()) {
      return Promise.resolve();
    }
    const contact = { ...this.person, metOn: this.person.metOn || null };
    const contacts = [...this.company().contacts];
    if (at === 'new') {
      contacts.push(contact);
    } else {
      contacts[at] = contact;
    }
    return this.write(contacts, 'That person could not be saved.');
  }

  protected remove(): Promise<void> {
    const at = this.editing();
    if (typeof at !== 'number') {
      return Promise.resolve();
    }
    const contacts = this.company().contacts.filter((_, i) => i !== at);
    return this.write(contacts, 'That person could not be removed.');
  }

  private async write(contacts: Company['contacts'], failure: string): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.store.saveCompany({ ...this.company(), contacts });
      this.editing.set(null);
    } catch (err) {
      this.error.set(messageOf(err, failure));
    } finally {
      this.busy.set(false);
    }
  }
}
