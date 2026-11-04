import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';

export type TrayAsk = { open: true } | { review: string };

/** Asks the AI tray to show something. The shell draws the tray, so any page can. */
@Injectable({ providedIn: 'root' })
export class LoopTray {
  readonly asks = new Subject<TrayAsk>();

  /** Opens its list, where running research shows how it's going. */
  open(): void {
    this.asks.next({ open: true });
  }

  /** Grows it to a finished research run's findings. */
  review(runId: string): void {
    this.asks.next({ review: runId });
  }
}
