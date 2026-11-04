import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  dayOfMonth,
  daysBetween,
  isWeekend,
  longDate,
  today,
  weekday,
  when,
} from '../../loop/days';
import { messageOf } from '../../loop/errors';
import { LoopEditors } from '../../loop/loop-editors';
import { LoopStore } from '../../loop/loop-store';
import {
  nextLabel,
  openOffers,
  plural,
  roundsAhead,
  searchDay,
  stageLabel,
  upcoming,
} from '../../loop/loop-summary';
import { statusOf } from '../../loop/model/status';
import { Todo } from '../../loop/model/todo';
import { Icon } from '../../shared/icon';
import { StageDot } from '../../shared/stage-dot';
import { Goals } from './goals';

const DAYS_AHEAD = 10;

@Component({
  selector: 'app-today',
  imports: [FormsModule, Goals, Icon, NgTemplateOutlet, RouterLink, StageDot],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto flex max-w-[1040px] flex-col gap-14 max-[719px]:gap-7">
      <header
        class="flex flex-wrap items-end justify-between gap-4 max-[719px]:flex-nowrap max-[719px]:items-start"
      >
        <div class="min-w-0">
          <p class="label m-0">{{ dateLine() }}</p>
          <h1 class="display mt-1.5 max-[719px]:text-[30px]">
            {{ headline() }}
            @if (offers().length) {
              <span class="text-[var(--color-faint)]"> · </span>
              <span class="text-[var(--color-accent)]">{{ offersLine() }}</span>
            }
          </h1>
        </div>
        <button type="button" class="btn btn-primary wide-only pl-3.5" (click)="editors.editNote()">
          <app-icon name="plus" [size]="14" [weight]="2" />Note
        </button>
      </header>

      <section [attr.aria-label]="'Next ' + daysAhead + ' days'" class="days">
        <div class="days-grid">
          @for (day of days(); track day.day) {
            @if (day.event) {
              <a
                class="day"
                [class]="day.classes"
                routerLink="/app/loop"
                [queryParams]="{ id: day.event.companyId, application: day.event.applicationId }"
                [attr.aria-label]="day.aria"
              >
                <ng-container *ngTemplateOutlet="cell; context: { $implicit: day }" />
              </a>
            } @else {
              <div class="day" [class]="day.classes">
                <ng-container *ngTemplateOutlet="cell; context: { $implicit: day }" />
              </div>
            }
          }
        </div>
        <ng-template #cell let-day>
          <span class="day-head"
            >{{ day.dow }}<br /><b>{{ day.date }}</b></span
          >
          <span class="day-track"
            ><span class="day-dot">{{ day.initial }}</span></span
          >
          <span class="day-what">
            <span class="day-who truncate">{{ day.who }}</span>
            <span class="day-kind truncate">{{ day.what }}</span>
          </span>
        </ng-template>
      </section>

      @if (offers().length) {
        <section aria-label="Offers" class="phone-only -mt-2 flex flex-col">
          @for (offer of offers(); track offer.id) {
            <a
              routerLink="/app/loop"
              [queryParams]="{ id: offer.companyId, application: offer.id }"
              class="flex min-h-[44px] items-center gap-3 text-[var(--color-text)] no-underline"
            >
              <span class="dot dot-offer" aria-hidden="true"></span>
              <span class="truncate flex-1 font-medium">{{ offer.company }} offer</span>
              <span class="mono text-[13px] text-[var(--color-accent)]">{{ nextOf(offer) }}</span>
            </a>
          }
        </section>
      }

      <app-goals />

      <div class="flex flex-wrap items-start gap-x-[72px] gap-y-12">
        <section aria-labelledby="loop-h" class="wide-only flex min-w-0 flex-[1_1_340px] flex-col">
          <h2 id="loop-h" class="label mb-2.5 mt-0">loop</h2>
          <div class="today-scroll today-scroll-loop">
            @for (row of loop(); track row.id) {
              <a
                class="row"
                routerLink="/app/loop"
                [queryParams]="{ id: row.companyId, application: row.id }"
                [attr.data-id]="row.id"
              >
                <app-stage-dot [status]="row.status" />
                <span class="truncate flex-1 font-medium">{{ row.company }}</span>
                <span class="meta w-16">{{ row.stage }}</span>
                <span class="meta w-16 text-right" [style.color]="row.nextColor">{{
                  row.next
                }}</span>
              </a>
            } @empty {
              <p class="row m-0 text-[var(--color-dim)]">
                @if (store.loaded()) {
                  {{ store.applications().length ? 'Nothing open.' : 'Nothing in the loop yet.' }}
                  @if (!store.companies().length) {
                    <a
                      routerLink="/app/loop"
                      class="btn-link inline-flex items-center text-[var(--color-accent)]"
                      >Add a company</a
                    >
                  }
                } @else {
                  Loading…
                }
              </p>
            }
          </div>
          <form class="row" (ngSubmit)="logApplication()">
            <label class="sr-only" for="new-application">Log an application at</label>
            <span class="flex w-[14px] flex-none justify-center text-[var(--color-faint)]"
              ><app-icon name="plus" [size]="14"
            /></span>
            <select
              id="new-application"
              name="companyId"
              class="input min-w-0 flex-1 border-0"
              [style.color]="newCompanyId() ? null : 'var(--color-faint)'"
              [(ngModel)]="newCompanyId"
            >
              <option value="">log an application at…</option>
              @for (company of store.companies(); track company.id) {
                <option [value]="company.id">{{ company.name }}</option>
              }
            </select>
            <label class="sr-only" for="new-company-applied">Applied</label>
            <input
              id="new-company-applied"
              name="appliedOn"
              type="date"
              required
              class="input mono w-[132px] flex-none border-0 text-[12px] text-[var(--color-dim)]"
              [(ngModel)]="newApplied"
            />
            <button
              type="submit"
              class="btn-link"
              [disabled]="!newCompanyId() || !newApplied() || logging()"
            >
              add
            </button>
          </form>
          @if (loopError()) {
            <p class="alert mt-3" role="alert">{{ loopError() }}</p>
          }
        </section>

        <section aria-labelledby="todo-h" class="flex min-w-0 flex-[1_1_340px] flex-col">
          <h2 id="todo-h" class="label mb-2.5 mt-0 flex justify-between">
            <span>to do</span><span>{{ openCount() }} left</span>
          </h2>
          <div class="today-scroll today-scroll-todo">
            @for (todo of todoRows(); track todo.id) {
              <div class="row todo" [class.done]="todo.done">
                <label>
                  <input
                    type="checkbox"
                    [checked]="todo.done"
                    (change)="toggle(todo, $any($event.target).checked)"
                  />
                  <span class="todo-label truncate flex-1">{{ todo.label }}</span>
                </label>
                <span class="meta w-12 text-right" [style.color]="todo.whenColor">{{
                  todo.when
                }}</span>
                <button
                  type="button"
                  class="todo-remove"
                  [attr.aria-label]="'Delete ' + todo.label"
                  (click)="remove(todo)"
                >
                  <app-icon name="close" [size]="14" />
                </button>
              </div>
            }
          </div>
          <form class="row gap-3" (ngSubmit)="add()">
            <label class="sr-only" for="new-todo">Add a to-do</label>
            <span class="flex w-4 flex-none justify-center text-[var(--color-faint)]"
              ><app-icon name="plus" [size]="14"
            /></span>
            <input
              id="new-todo"
              name="label"
              class="input min-w-0 flex-1 border-0"
              placeholder="add a to-do"
              autocomplete="off"
              [(ngModel)]="newLabel"
            />
            <label class="sr-only" for="new-todo-due">Due</label>
            <input
              id="new-todo-due"
              name="due"
              type="date"
              class="input mono w-[132px] flex-none border-0 text-[12px] text-[var(--color-dim)]"
              [(ngModel)]="newDue"
            />
            <button type="submit" class="btn-link" [disabled]="!newLabel().trim() || adding()">
              add
            </button>
          </form>
          @if (todoError()) {
            <p class="alert mt-3" role="alert">{{ todoError() }}</p>
          }
        </section>
      </div>
    </div>
  `,
})
export class Today {
  protected readonly store = inject(LoopStore);
  protected readonly editors = inject(LoopEditors);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  protected readonly daysAhead = DAYS_AHEAD;

  protected readonly newLabel = signal('');
  protected readonly newDue = signal('');
  protected readonly adding = signal(false);
  protected readonly todoError = signal('');

  protected readonly newCompanyId = signal('');
  protected readonly newApplied = signal(today());
  protected readonly logging = signal(false);
  protected readonly loopError = signal('');

  protected readonly offers = computed(() => openOffers(this.store.applications()));

  protected readonly dateLine = computed(() => {
    const now = this.store.today();
    const day = searchDay(this.store.applications(), now);
    return day ? `${longDate(now)} · day ${day}` : longDate(now);
  });

  protected readonly headline = computed(() => {
    const rounds = roundsAhead(this.store.applications(), this.store.today(), DAYS_AHEAD);
    if (!this.store.loaded()) {
      return '…';
    }
    return rounds === 0 && this.offers().length === 0 ? 'No rounds ahead' : plural(rounds, 'round');
  });

  protected readonly offersLine = computed(() => plural(this.offers().length, 'offer'));

  protected readonly days = computed(() => {
    const now = this.store.today();
    return upcoming(this.store.applications(), now, DAYS_AHEAD).map((day, index) => {
      const event = day.events.find((e) => e.offer) ?? day.events[0] ?? null;
      const extra = day.events.length > 1 ? ` +${day.events.length - 1}` : '';
      const classes = [
        index === 0 ? 'is-today' : '',
        isWeekend(day.day) ? 'weekend' : '',
        event ? 'has-event' : '',
        event?.offer ? 'has-offer' : '',
      ].join(' ');
      return {
        day: day.day,
        dow: index === 0 ? 'today' : weekday(day.day),
        date: dayOfMonth(day.day),
        classes,
        event,
        initial: event?.company[0]?.toUpperCase() ?? '',
        who: event ? event.company + extra : '',
        what: event?.what ?? '',
        aria:
          day.events.map((e) => `${e.company}, ${e.what}`).join('; ') + `, ${longDate(day.day)}`,
      };
    });
  });

  protected readonly loop = computed(() => {
    const now = this.store.today();
    return this.store
      .applications()
      .filter((application) => !application.closed)
      .map((application) => {
        const status = statusOf(application);
        return {
          id: application.id,
          companyId: application.companyId,
          company: application.company,
          status,
          stage: stageLabel(application),
          next: nextLabel(application, now),
          nextColor:
            status === 'offer'
              ? 'var(--color-accent)'
              : status === 'active' && application.nextOn
                ? 'var(--color-amber)'
                : 'var(--color-faint)',
        };
      });
  });

  protected readonly todoRows = computed(() => {
    const now = this.store.today();
    return this.store.todos().map((todo) => {
      const due = todo.dueOn;
      const pressing = !!due && daysBetween(now, due) <= 0;
      return {
        ...todo,
        when: due ? when(due, now) : '',
        whenColor: todo.done
          ? 'var(--color-faint)'
          : pressing
            ? 'var(--color-amber)'
            : 'var(--color-muted)',
      };
    });
  });

  protected readonly openCount = computed(() => this.store.todos().filter((t) => !t.done).length);

  protected nextOf(application: Parameters<typeof nextLabel>[0]): string {
    return nextLabel(application, this.store.today());
  }

  protected async toggle(todo: Todo, done: boolean): Promise<void> {
    this.todoError.set('');
    try {
      await this.store.setTodoDone(todo, done);
    } catch (err) {
      this.todoError.set(messageOf(err, 'That to-do could not be saved.'));
    }
  }

  protected async remove(todo: Todo): Promise<void> {
    this.todoError.set('');
    try {
      await this.store.deleteTodo(todo.id);
    } catch (err) {
      this.todoError.set(messageOf(err, 'That to-do could not be deleted.'));
    }
  }

  protected async add(): Promise<void> {
    const label = this.newLabel().trim();
    if (!label || this.adding()) {
      return;
    }
    this.adding.set(true);
    this.todoError.set('');
    try {
      await this.store.addTodo(label, this.newDue() || null);
      this.newLabel.set('');
      this.newDue.set('');
    } catch (err) {
      this.todoError.set(messageOf(err, 'That to-do could not be added.'));
    } finally {
      this.adding.set(false);
    }
  }

  protected async logApplication(): Promise<void> {
    const companyId = this.newCompanyId();
    if (!companyId || !this.newApplied() || this.logging()) {
      return;
    }
    this.logging.set(true);
    this.loopError.set('');
    try {
      const saved = await this.store.addApplication(companyId, { appliedOn: this.newApplied() });
      this.newCompanyId.set('');
      this.newApplied.set(today());
      // The loop orders by stage, so a fresh application can land below the fold.
      afterNextRender(
        () =>
          this.host.nativeElement
            .querySelector(`[data-id="${saved.id}"]`)
            ?.scrollIntoView({ block: 'nearest' }),
        { injector: this.injector },
      );
    } catch (err) {
      this.loopError.set(messageOf(err, 'That application could not be added.'));
    } finally {
      this.logging.set(false);
    }
  }
}
