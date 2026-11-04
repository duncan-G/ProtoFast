import { ChangeDetectionStrategy, Component, model } from '@angular/core';
import { Review } from '../../loop/findings';

/** What research found, each ticked to keep or not, with where it came from. */
@Component({
  selector: 'app-findings-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-[22px]' },
  template: `
    @let shown = review();
    @if (shown.note) {
      <p class="research-note">{{ shown.note }}</p>
    }
    @if (shown.findings.length) {
      <ul class="findings">
        @for (finding of shown.findings; track $index; let i = $index) {
          @if (headingAt(i); as caption) {
            <li class="caption findings-group">{{ caption }}</li>
          }
          <li>
            <label class="finding">
              <input
                type="checkbox"
                class="finding-check"
                [checked]="finding.keep"
                (change)="toggle(i)"
              />
              <span class="finding-label" [class.blank]="!labelAt(i)">{{ labelAt(i) }}</span>
              <span class="finding-value">
                <span [class.finding-long]="finding.long">{{ finding.text }}</span>
                @if (finding.replaces) {
                  <span class="finding-was">was {{ finding.replaces }}</span>
                }
              </span>
            </label>
          </li>
        }
      </ul>
    } @else {
      <p class="m-0 text-[var(--color-dim)]">Nothing turned up that you don't have.</p>
    }
    @if (shown.sources.length) {
      <p class="research-sources">
        from
        @for (source of shown.sources; track source.url; let last = $last) {
          <a [href]="source.url" target="_blank" rel="noopener noreferrer">{{
            source.title || hostOf(source.url)
          }}</a
          >{{ last ? '' : ' · ' }}
        }
      </p>
    }
  `,
})
export class FindingsList {
  readonly review = model.required<Review>();

  protected toggle(index: number): void {
    this.review.update((review) => ({
      ...review,
      findings: review.findings.map((f, i) => (i === index ? { ...f, keep: !f.keep } : f)),
    }));
  }

  /** A caption where the findings move from one record to the other. */
  protected headingAt(index: number): string {
    const findings = this.review().findings;
    const targets = new Set(findings.map((f) => f.target));
    if (targets.size < 2 || findings[index - 1]?.target === findings[index].target) {
      return '';
    }
    return findings[index].target === 'company' ? 'the company' : 'the job';
  }

  /** A list's label shows on its first item only. */
  protected labelAt(index: number): string {
    const findings = this.review().findings;
    const finding = findings[index];
    const previous = findings[index - 1];
    return finding.list && previous?.key === finding.key && previous.target === finding.target
      ? ''
      : finding.label;
  }

  protected hostOf(url: string): string {
    try {
      return new URL(url).host.replace(/^www\./, '');
    } catch {
      return url;
    }
  }
}
