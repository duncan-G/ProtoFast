import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { shortDate } from '../../loop/days';
import { newestFirst } from '../../loop/learnings';
import { Application } from '../../loop/model/application';
import { Icon } from '../../shared/icon';
import { LearningAdd } from './learning-add';

/** The newest few things learned; the rest in the log. */
@Component({
  selector: 'app-learnings-card',
  imports: [Icon, LearningAdd, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'card card-pad', role: 'region', 'aria-label': 'What you learned' },
  template: `
    <div class="card-head">
      <h3 class="card-title">
        What you learned
        @if (count()) {
          <span class="badge">{{ count() }}</span>
        }
      </h3>
      @if (count() > 1) {
        <span class="hint">Newest first</span>
      }
    </div>
    @if (newest().length) {
      <ul class="learned-list">
        @for (entry of newest(); track entry.at) {
          <li>
            @if (entry.round || entry.on) {
              <p class="learned-meta">
                @if (entry.round) {
                  <span class="round-tag">{{ entry.round }}</span>
                }
                @if (entry.on) {
                  <span class="meta">{{ shortDate(entry.on) }}</span>
                }
              </p>
            }
            <p class="learned-text">{{ entry.text }}</p>
          </li>
        }
      </ul>
      <div class="card-actions">
        <a
          class="btn btn-xs"
          routerLink="/app/loop"
          [queryParams]="{ view: 'learned' }"
          queryParamsHandling="merge"
          >{{ count() > limit ? 'See all ' + count() : 'Open the log'
          }}<app-icon name="next" [size]="13"
        /></a>
      </div>
    } @else {
      <p class="quiet">After a round, jot down what came up so the next one goes better.</p>
    }
    <app-learning-add [application]="application()" />
  `,
})
export class LearningsCard {
  readonly application = input.required<Application>();

  protected readonly limit = 3;
  protected readonly shortDate = shortDate;
  protected readonly count = computed(() => this.application().learnings.length);
  protected readonly newest = computed(() =>
    newestFirst(this.application().learnings).slice(0, this.limit),
  );
}
