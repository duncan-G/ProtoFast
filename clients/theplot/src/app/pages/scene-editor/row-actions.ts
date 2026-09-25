import { ChangeDetectionStrategy, Component, output } from '@angular/core';

@Component({
  selector: 'app-row-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'se-card-actions' },
  template: `
    <button
      type="button"
      class="se-btn se-btn-icon"
      title="Move up"
      aria-label="Move up"
      (click)="up.emit()"
    >
      ↑
    </button>
    <button
      type="button"
      class="se-btn se-btn-icon"
      title="Move down"
      aria-label="Move down"
      (click)="down.emit()"
    >
      ↓
    </button>
    <button type="button" class="se-btn se-btn-danger" (click)="remove.emit()">Delete</button>
    <button type="button" class="se-btn" aria-pressed="true" (click)="done.emit()">Done</button>
  `,
})
export class RowActions {
  readonly up = output();
  readonly down = output();
  readonly remove = output();
  readonly done = output();
}
