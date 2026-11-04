import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  input,
  model,
  viewChild,
} from '@angular/core';
import { addDays, dayOfMonth, isWeekend, shortDate, today, weekday } from '../loop/days';
import { Icon } from '../shared/icon';

interface Pick {
  day: string;
  label: string;
}

/** A day as quick picks or one button, either opening the calendar for any other; '' is none. */
@Component({
  selector: 'app-date-field',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    @if (picks()) {
      <div role="radiogroup" class="picks" [attr.aria-labelledby]="labelledBy()">
        @for (pick of quick(); track pick.day) {
          <button
            type="button"
            role="radio"
            class="pick"
            [class.pick-hot]="hot()"
            [attr.aria-checked]="value() === pick.day"
            (click)="value.set(pick.day)"
          >
            {{ pick.label }}
          </button>
        }
        <span class="relative inline-flex">
          <button
            type="button"
            role="radio"
            class="pick"
            [class.pick-open]="!other()"
            [class.pick-hot]="hot()"
            [attr.aria-checked]="!!other()"
            (click)="open()"
          >
            <app-icon name="today" [size]="13" />{{ other() ? display(other()) : 'pick a day' }}
          </button>
          <input
            #calendar
            type="date"
            class="date-native"
            tabindex="-1"
            aria-hidden="true"
            [value]="value()"
            (change)="value.set($any($event.target).value)"
          />
        </span>
        @if (none()) {
          <button
            type="button"
            role="radio"
            class="pick"
            [attr.aria-checked]="!value()"
            (click)="value.set('')"
          >
            {{ none() }}
          </button>
        }
      </div>
    } @else {
      <span class="relative block">
        <button
          type="button"
          class="date-button"
          [attr.aria-label]="label() + ': ' + (value() ? display(value()) : 'no day') + '. Change'"
          (click)="open()"
        >
          <span>{{ value() ? display(value()) : 'pick a day' }}</span>
          <app-icon name="today" [size]="15" />
        </button>
        <input
          #calendar
          type="date"
          class="date-native"
          tabindex="-1"
          aria-hidden="true"
          [value]="value()"
          (change)="value.set($any($event.target).value)"
        />
      </span>
    }
  `,
})
export class DateField {
  private readonly calendar = viewChild.required<ElementRef<HTMLInputElement>>('calendar');

  readonly value = model('');
  readonly label = input.required<string>();
  readonly labelledBy = input('');
  /** The days just gone, or the days ahead; none shows one button. */
  readonly picks = input<'past' | 'ahead' | null>(null);
  /** A pick that clears the day, named for what that means here. */
  readonly none = input('');
  /** Picked days in amber, for what's coming. */
  readonly hot = input(false);

  protected readonly quick = computed((): Pick[] => {
    const now = today();
    if (this.picks() === 'past') {
      return [
        { day: now, label: 'today' },
        { day: addDays(now, -1), label: 'yesterday' },
      ];
    }
    const days: Pick[] = [
      { day: now, label: 'today' },
      { day: addDays(now, 1), label: 'tomorrow' },
    ];
    for (let day = addDays(now, 2); days.length < 5; day = addDays(day, 1)) {
      if (!isWeekend(day)) {
        days.push({ day, label: `${weekday(day)} ${dayOfMonth(day)}` });
      }
    }
    return days;
  });

  /** A day none of the quick picks covers. */
  protected readonly other = computed(() => {
    const day = this.value();
    return day && !this.quick().some((pick) => pick.day === day) ? day : '';
  });

  protected display(day: string): string {
    return `${weekday(day)} · ${shortDate(day)}`;
  }

  protected open(): void {
    const calendar = this.calendar().nativeElement;
    try {
      calendar.showPicker();
    } catch {
      calendar.focus();
      calendar.click();
    }
  }
}
