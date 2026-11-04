import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FieldsForm } from '../../editors/fields-form';
import { companySections } from '../../loop/company-sections';
import { shortDate, when, whenWithDate } from '../../loop/days';
import { LoopStore } from '../../loop/loop-store';
import { nextLabel, openOffers, plural, searchDay, stageLabel } from '../../loop/loop-summary';
import { Application } from '../../loop/model/application';
import { STAGES } from '../../loop/model/stage';
import { statusOf, STATUSES } from '../../loop/model/status';
import { Icon } from '../../shared/icon';
import { riseIn } from '../../shared/rise-in';
import { StageDot } from '../../shared/stage-dot';
import { passes, StageFilter, StatusFilter } from '../../shared/stage-filter';
import { COMPANY_FIELDS, CompanyView } from './company-view';

/** The loop: the companies in sections by status, and one company when picked. */
@Component({
  selector: 'app-loop',
  imports: [CompanyView, FieldsForm, Icon, RouterLink, StageDot, StageFilter],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div #view class="mx-auto flex max-w-[1120px] flex-col gap-11 max-[719px]:gap-7">
      @if (picked(); as id) {
        @if (store.companiesById().get(id); as company) {
          <!-- One view per company, so a panel open on one never shows on the next. -->
          @for (key of [id]; track key) {
            <app-company-view [company]="company" [applicationId]="applicationParam()" />
          }
        } @else {
          <p class="m-0 py-3.5 text-[var(--color-dim)]">
            @if (store.loaded()) {
              That company isn’t in the loop.
              <a routerLink="/app/loop" class="text-[var(--color-accent)]">Back to the loop</a>
            } @else {
              Loading…
            }
          </p>
        }
      } @else {
        <header class="flex items-end justify-between gap-4">
          <div>
            @if (day(); as day) {
              <p class="label m-0">day {{ day }}</p>
            }
            <h1 class="display mt-1.5 max-[719px]:text-[30px]">
              {{ openLine() }}
              @if (offers()) {
                <span class="text-[var(--color-faint)]"> · </span>
                <span class="text-[var(--color-accent)]"
                  >{{ offers() }} way{{ offers() === 1 ? '' : 's' }} out</span
                >
              }
            </h1>
          </div>
          <button type="button" class="btn btn-primary wide-only pl-3.5" (click)="adding.set(true)">
            <app-icon name="plus" [size]="14" [weight]="2" />Company
          </button>
          <button
            type="button"
            class="btn btn-icon phone-only text-[var(--color-text)]"
            aria-label="Add a company"
            (click)="adding.set(true)"
          >
            <app-icon name="plus" [size]="16" [weight]="1.8" />
          </button>
        </header>

        <section aria-label="Companies" class="flex min-w-0 flex-col">
          @if (adding()) {
            <app-fields-form
              sheet
              heading="new company"
              note="numbers, people and applications go on its page"
              [fields]="newCompany"
              [value]="{ name: '', about: '' }"
              [save]="addCompany"
              saveLabel="Add company"
              failure="That company could not be added."
              (closed)="adding.set(false)"
            />
          }
          <app-stage-filter class="mb-1" [status]="status()" (statusChange)="setStatus($event)" />
          @for (section of sections(); track section.status) {
            <h2
              class="label mb-1 flex gap-2 px-3 max-[719px]:px-0"
              [class.mt-4]="$first"
              [class.mt-8]="!$first"
            >
              <span>{{ section.label }}</span>
              <span class="text-[var(--color-faint)]">{{ section.rows.length }}</span>
            </h2>
            @for (row of section.rows; track row.id) {
              <a
                class="row-pick max-[719px]:min-h-[64px] max-[719px]:rounded-none max-[719px]:border-t max-[719px]:border-[var(--color-line)] max-[719px]:px-0"
                routerLink="/app/loop"
                [queryParams]="{ id: row.id }"
                queryParamsHandling="merge"
                [attr.data-company]="row.id"
                [class.row-starred]="row.starred"
                [style.color]="row.status === 'closed' ? 'var(--color-dim)' : null"
              >
                @if (row.starred) {
                  <app-icon name="star" class="star-mark w-[14px] justify-center" [size]="14" />
                  <span class="sr-only">starred</span>
                } @else {
                  <app-stage-dot [status]="row.status" />
                }
                <span class="flex min-w-0 flex-1 items-baseline gap-x-6 max-[719px]:flex-col">
                  <span class="w-[34%] flex-none truncate font-medium max-[719px]:w-full">{{
                    row.name
                  }}</span>
                  <span
                    class="wide-only min-w-0 flex-1 truncate text-[14px] text-[var(--color-dim)]"
                    >{{ row.detail }}</span
                  >
                  <span
                    class="phone-only mt-1.5 flex gap-1"
                    role="img"
                    [attr.aria-label]="row.stage"
                  >
                    @for (pip of row.pips; track $index) {
                      <span class="h-[3px] w-3.5 rounded-sm" [style.background]="pip"></span>
                    }
                  </span>
                </span>
                <span class="meta wide-only w-[64px]">{{ row.stage }}</span>
                <span class="meta wide-only w-[64px] text-right" [style.color]="row.nextColor">{{
                  row.next
                }}</span>
                <span class="mono phone-only text-right text-[11.5px] leading-[1.5]">
                  <span class="block text-[var(--color-dim)]">{{ row.stage }}</span>
                  <span class="block" [style.color]="row.nextColor">{{ row.when }}</span>
                </span>
              </a>
            }
          } @empty {
            <p class="m-0 py-3.5 text-[var(--color-dim)]">
              @if (store.loaded() && status() && store.companies().length) {
                Nothing at this stage.
                <button
                  type="button"
                  class="btn-link text-[15px] text-[var(--color-accent)]"
                  (click)="setStatus('all')"
                >
                  Show all
                </button>
              } @else if (store.loaded() && store.companies().length) {
                Nothing open.
                <button
                  type="button"
                  class="btn-link text-[15px] text-[var(--color-accent)]"
                  (click)="setStatus('closed')"
                >
                  Show closed
                </button>
              } @else if (store.loaded()) {
                Nothing in the loop yet.
                <button
                  type="button"
                  class="btn-link text-[15px] text-[var(--color-accent)]"
                  (click)="adding.set(true)"
                >
                  Add your first company
                </button>
              } @else {
                Loading…
              }
            </p>
          }
        </section>
      }
    </div>
  `,
})
export class Loop {
  protected readonly store = inject(LoopStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly params = toSignal(this.route.queryParamMap, { requireSync: true });
  private readonly view = viewChild.required<ElementRef<HTMLElement>>('view');
  private lastPicked: string | null = null;
  private rendered = false;

  protected readonly newCompany = COMPANY_FIELDS;
  protected readonly adding = signal(false);

  protected readonly picked = computed(() => this.params().get('id'));
  protected readonly applicationParam = computed(() => this.params().get('application'));
  protected readonly status = computed(() => this.params().get('status') as StatusFilter | null);
  protected readonly day = computed(() => searchDay(this.store.applications(), this.store.today()));
  protected readonly offers = computed(() => openOffers(this.store.applications()).length);
  protected readonly openLine = computed(() =>
    this.store.loaded() ? `${this.store.applications().filter((a) => !a.closed).length} open` : '…',
  );

  /** An existing name opens that company rather than adding it twice. */
  protected readonly addCompany = async (fields: Record<string, unknown>) => {
    const name = String(fields['name']).trim();
    const existing = this.store
      .companies()
      .find((c) => c.name.localeCompare(name, undefined, { sensitivity: 'base' }) === 0);
    const company = existing ?? (await this.store.addCompany(name, String(fields['about'] ?? '')));
    // The company replaces the list before the sheet's dialog fires its close event.
    this.adding.set(false);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { id: company.id, status: null },
      queryParamsHandling: 'merge',
    });
  };

  protected readonly sections = computed(() => {
    const now = this.store.today();
    const filter = this.status();
    return companySections(this.store.companies(), this.store.applications())
      .filter((section) => passes(section.status, filter))
      .map((section) => ({
        status: section.status,
        label: STATUSES.find((s) => s.status === section.status)?.label ?? section.status,
        rows: section.entries.map(({ company, applications, lead, status, starred }) => ({
          id: company.id,
          name: company.name,
          status,
          starred,
          detail: lead
            ? [
                lead.role || 'role not set',
                applications.length > 1 && plural(applications.length, 'application'),
              ]
                .filter(Boolean)
                .join(' · ')
            : 'no application yet',
          stage: lead ? stageLabel(lead) : '—',
          next: lead ? nextLabel(lead, now) : '',
          when: lead ? this.rowWhen(lead, now) : '',
          nextColor:
            status === 'offer'
              ? 'var(--color-accent)'
              : status === 'active' && lead?.nextOn
                ? 'var(--color-amber)'
                : 'var(--color-faint)',
          pips: this.pips(lead),
        })),
      }));
  });

  constructor() {
    afterRenderEffect(() => {
      const picked = this.picked();
      untracked(() => {
        // The shell eases the page in when it arrives; this is the list ↔ company step.
        if (this.rendered) {
          riseIn(this.view().nativeElement);
        }
        this.rendered = true;
        if (picked) {
          this.lastPicked = picked;
          window.scrollTo(0, 0);
        } else if (this.lastPicked) {
          this.host.nativeElement
            .querySelector(`[data-company="${this.lastPicked}"]`)
            ?.scrollIntoView({ block: 'center' });
        }
      });
    });
  }

  protected setStatus(status: StatusFilter | null): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { status, id: null, application: null, note: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  private pips(lead: Application | null): string[] {
    const status = lead ? statusOf(lead) : 'none';
    if (!lead || status === 'none') {
      return STAGES.map(() => 'var(--color-line-2)');
    }
    const at = STAGES.indexOf(lead.stage);
    const hot =
      status === 'offer'
        ? 'var(--color-accent)'
        : status === 'active'
          ? 'var(--color-amber)'
          : status === 'applied'
            ? 'var(--color-muted)'
            : 'var(--color-edge-2)';
    return STAGES.map((_, i) =>
      i < at
        ? status === 'closed'
          ? 'var(--color-edge)'
          : 'var(--color-faint)'
        : i === at
          ? hot
          : 'var(--color-line-2)',
    );
  }

  private rowWhen(application: Application, now: string): string {
    if (application.closed) {
      return application.appliedOn || application.stage !== 'applied'
        ? `after ${application.stage}`
        : 'not applied';
    }
    if (application.stage === 'offer') {
      return nextLabel(application, now);
    }
    if (application.nextOn) {
      return whenWithDate(application.nextOn, now);
    }
    if (!application.appliedOn) {
      return '';
    }
    return application.stage === 'applied'
      ? shortDate(application.appliedOn)
      : when(application.appliedOn, now);
  }
}
