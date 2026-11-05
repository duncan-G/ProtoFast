import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FieldSpec, FieldsForm } from '../../editors/fields-form';
import { ListCard } from '../../editors/list-card';
import { LoopStore } from '../../loop/loop-store';
import { Company } from '../../loop/model/company';
import { figureParts } from '../../shared/figure';
import { Icon } from '../../shared/icon';
import { People } from './people';

type Section = 'business' | 'numbers';

const BUSINESS: readonly FieldSpec[] = [
  {
    key: 'whatItDoes',
    label: 'what it does',
    hint: 'in plain words',
    type: 'textarea',
    placeholder: 'Runs card payments for online shops and pays them out the next day.',
  },
  {
    key: 'howItMakesMoney',
    label: 'how it makes money',
    hint: 'who pays, for what',
    type: 'textarea',
    placeholder: 'Takes 2.9% + 30¢ of each payment; big shops pay a monthly fee instead.',
  },
];

const NUMBERS: readonly FieldSpec[] = [
  { key: 'revenue', label: 'revenue', placeholder: '$140M', look: 'figure' },
  { key: 'growth', label: 'growth target', placeholder: '2× by ’27', look: 'figure' },
  { key: 'headcount', label: 'people', placeholder: '~400', look: 'figure' },
  { key: 'funding', label: 'funding', placeholder: 'Series C', look: 'figure' },
  { key: 'lastRaised', label: 'last raised', placeholder: 'Mar 2025', look: 'figure' },
];

const FIGURES = [
  ['revenue', 'Revenue'],
  ['headcount', 'People'],
  ['funding', 'Funding'],
  ['growth', 'Growth target'],
] as const;

/** What you know about the company, apart from any application to it. */
@Component({
  selector: 'app-company-about',
  imports: [FieldsForm, Icon, ListCard, People],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dim-scope flex flex-col gap-6' },
  template: `
    @let co = company();
    <section class="card card-pad dim-scope" aria-label="The business">
      @if (editing() === 'business') {
        <app-fields-form
          heading="the business"
          [fields]="businessFields"
          [value]="co"
          [save]="saveChanges"
          (closed)="editing.set(null)"
        />
      } @else {
        <div class="card-head">
          <h3 class="card-title">The business</h3>
          <button type="button" class="btn btn-xs" (click)="editing.set('business')">
            <app-icon name="edit" [size]="13" />Edit
          </button>
        </div>
        <div class="preview-columns">
          @for (part of business(); track part.label) {
            <div class="flex min-w-0 flex-col gap-1.5">
              <h4 class="field-label">{{ part.label }}</h4>
              @if (part.text) {
                <p class="m-0 text-[15px] leading-[1.55] text-[var(--color-text-2)]">
                  {{ part.text }}
                </p>
              } @else {
                <button type="button" class="btn-add self-start" (click)="editing.set('business')">
                  <app-icon name="plus" [size]="11" [weight]="2" />Add
                </button>
              }
            </div>
          }
        </div>
      }
    </section>

    <section class="card card-pad dim-scope" aria-label="Numbers">
      @if (editing() === 'numbers') {
        <app-fields-form
          heading="numbers"
          [fields]="numberFields"
          [value]="co"
          [save]="saveChanges"
          (closed)="editing.set(null)"
        />
      } @else {
        <div class="card-head">
          <h3 class="card-title">Numbers</h3>
          <button type="button" class="btn btn-xs" (click)="editing.set('numbers')">
            <app-icon name="edit" [size]="13" />Edit
          </button>
        </div>
        <dl class="tiles">
          @for (figure of figures(); track figure.label) {
            <div class="tile" [class.is-empty]="!figure.value">
              <dt class="field-label">{{ figure.label }}</dt>
              @if (figure.value) {
                <dd class="figure">{{ figure.value }}</dd>
                @if (figure.context) {
                  <dd class="tile-context">{{ figure.context }}</dd>
                }
              } @else {
                <dd>
                  <button type="button" class="btn-add min-h-8" (click)="editing.set('numbers')">
                    <app-icon name="plus" [size]="11" [weight]="2" />Add
                  </button>
                </dd>
              }
            </div>
          }
        </dl>
      }
    </section>

    <div class="card-columns dim-scope">
      <app-people [company]="co" />
      <app-list-card
        heading="Competes with"
        look="tags"
        placeholder="Stripe"
        empty="No competitors noted."
        [subtitle]="co.name"
        [items]="co.competitors"
        [save]="saveList('competitors')"
      />
      <app-list-card
        heading="Concerns"
        marker="!"
        placeholder="Layoffs in March"
        empty="Nothing worrying noted."
        [subtitle]="co.name"
        [items]="co.concerns"
        [save]="saveList('concerns')"
      />
      <app-list-card
        heading="Values"
        placeholder="Default to writing"
        empty="No values noted."
        [subtitle]="co.name"
        [items]="co.values"
        [save]="saveList('values')"
      />
      <app-list-card
        heading="How it works"
        placeholder="On-call one week in six"
        empty="Nothing noted on how they work."
        [subtitle]="co.name"
        [items]="co.practices"
        [save]="saveList('practices')"
      />
    </div>
  `,
})
export class CompanyAbout {
  private readonly store = inject(LoopStore);

  readonly company = input.required<Company>();

  protected readonly businessFields = BUSINESS;
  protected readonly numberFields = NUMBERS;
  protected readonly editing = signal<Section | null>(null);

  protected readonly saveChanges = (changes: Record<string, unknown>) =>
    this.store.saveCompany({ ...this.company(), ...changes });

  protected readonly saveList =
    (key: 'competitors' | 'concerns' | 'values' | 'practices') =>
    (items: string[]): Promise<unknown> =>
      this.saveChanges({ [key]: items });

  protected readonly business = computed(() => {
    const co = this.company();
    return [
      { label: 'What it does', text: co.whatItDoes },
      { label: 'How it makes money', text: co.howItMakesMoney },
    ];
  });

  /** "[FY2025]" context goes under the figure; funding's under it is when it was raised. */
  protected readonly figures = computed(() => {
    const co = this.company();
    return FIGURES.map(([key, label]) => {
      const { figure, context } = figureParts(co[key]);
      const raised = key === 'funding' && co.lastRaised ? co.lastRaised : '';
      return {
        label,
        value: figure,
        context: [context, raised].filter(Boolean).join(' · '),
      };
    });
  });
}
