import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type IconName =
  | 'search'
  | 'today'
  | 'notes'
  | 'loop'
  | 'plus'
  | 'briefcase'
  | 'edit'
  | 'account'
  | 'trash'
  | 'back'
  | 'down'
  | 'close'
  | 'key'
  | 'mail'
  | 'file'
  | 'upload'
  | 'pin'
  | 'star';

/** The design's stroke icons, drawn in currentColor. Decorative: label the control instead. */
@Component({
  selector: 'app-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-flex flex-none', 'aria-hidden': 'true' },
  template: `
    <svg
      [attr.width]="size()"
      [attr.height]="size()"
      viewBox="0 0 16 16"
      fill="none"
      stroke="currentColor"
      [attr.stroke-width]="weight()"
      stroke-linecap="round"
      stroke-linejoin="round"
    >
      @switch (name()) {
        @case ('search') {
          <circle cx="7" cy="7" r="4.5" />
          <path d="m10.5 10.5 3 3" />
        }
        @case ('today') {
          <rect x="2" y="3" width="12" height="11" rx="2" />
          <path d="M2 6.5h12M5.5 1.5v3M10.5 1.5v3" />
        }
        @case ('notes') {
          <path d="M4 1.75h5.5L12.5 4.75v9.5H4z" />
          <path d="M6.5 8h4M6.5 11h3" />
        }
        @case ('loop') {
          <path d="M13 8a5 5 0 1 1-1.6-3.7" />
          <path d="M11.8 1.6v2.9H8.9" />
        }
        @case ('plus') {
          <path d="M8 3v10M3 8h10" />
        }
        @case ('briefcase') {
          <rect x="2" y="4.5" width="12" height="9" rx="1.5" />
          <path d="M5.5 4.5V3a1 1 0 0 1 1-1h3a1 1 0 0 1 1 1v1.5" />
        }
        @case ('edit') {
          <path d="M10.5 2.5l3 3L6 13H3v-3z" />
        }
        @case ('account') {
          <circle cx="8" cy="5.7" r="2.45" />
          <path d="M3.1 13.4a5.1 5.1 0 0 1 9.8 0" />
        }
        @case ('trash') {
          <path d="M2.5 4.5h11M6 4.5V3h4v1.5M4 4.5l.7 9h6.6l.7-9" />
        }
        @case ('back') {
          <path d="M10 3 5 8l5 5" />
        }
        @case ('down') {
          <path d="m4 6 4 4 4-4" />
        }
        @case ('close') {
          <path d="m4 4 8 8M12 4l-8 8" />
        }
        @case ('key') {
          <circle cx="5.3" cy="8" r="2.7" />
          <path d="M8 8h6M12 8v2M14 8v1.3" />
        }
        @case ('mail') {
          <rect x="2.3" y="3.7" width="11.4" height="8.6" rx="1.4" />
          <path d="m2.7 4.7 5.3 4 5.3-4" />
        }
        @case ('file') {
          <path d="M4 1.75h5.5L12.5 4.75v9.5H4z" />
          <path d="M9.25 1.75v3.25h3.25" />
        }
        @case ('upload') {
          <path d="M8 10.5v-8M4.75 5.5 8 2.25l3.25 3.25" />
          <path d="M2.5 10.5v2.25a1 1 0 0 0 1 1h9a1 1 0 0 0 1-1V10.5" />
        }
        @case ('pin') {
          <path d="M13 6.75c0 3.5-5 7.5-5 7.5s-5-4-5-7.5a5 5 0 0 1 10 0z" />
          <circle cx="8" cy="6.75" r="1.75" />
        }
        @case ('star') {
          <path
            d="M8 2.1 9.6 6.2 14 6.45 10.6 9.25 11.7 13.5 8 11.1 4.3 13.5 5.4 9.25 2 6.45 6.4 6.2z"
          />
        }
      }
    </svg>
  `,
})
export class Icon {
  readonly name = input.required<IconName>();
  readonly size = input(18);
  readonly weight = input(1.5);
}
