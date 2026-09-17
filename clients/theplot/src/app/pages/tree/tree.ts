import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AppShell } from '../../shared/app-shell';
import { TreeView } from '../../shared/tree-view';
import { SegmentationApi } from '../../segmentation/segmentation-api';
import type { Paragraph, Result, SectionNode } from '../../../lib/gen/segmentation_pb';

/**
 * The tree beside the text: the outline on the left, the selected section's paragraphs on the
 * right. Selecting a section scrolls its first paragraph into view rather than filtering the
 * reading pane, so the document still reads as a document.
 */
@Component({
  selector: 'app-tree-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AppShell, TreeView, RouterLink],
  template: `
    <app-shell>
      @if (result(); as current) {
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div class="min-w-0">
            <h1 class="truncate text-2xl font-semibold tracking-tight">{{ current.root?.title }}</h1>
            <p class="mt-1 flex flex-wrap items-center gap-x-3 text-sm text-[var(--color-neutral-500)]">
              <span>{{ current.paragraphs.length }} paragraphs</span>
              <span>{{ sectionCount() }} sections</span>
              <span class="font-mono text-xs" title="The content hash this structure was frozen at">
                {{ current.treeHash.slice(0, 12) }}
              </span>
            </p>
          </div>
          <a [routerLink]="['/app/runs', runId]" class="btn btn-secondary">Run details</a>
        </div>

        @if (current.findings.length > 0) {
          <details class="mt-6 rounded-[var(--radius-lg)] border border-[var(--color-divider)] px-4 py-3">
            <summary class="cursor-pointer text-sm">
              {{ current.findings.length }} reviewer
              {{ current.findings.length === 1 ? 'finding' : 'findings' }}
            </summary>
            <ul class="mt-3 space-y-2 text-sm">
              @for (finding of current.findings; track $index) {
                <li class="flex gap-2">
                  <span class="chip shrink-0">{{ finding.severity }}</span>
                  <span class="min-w-0">
                    <span class="text-[var(--color-neutral-400)]">{{ finding.message }}</span>
                    <span class="ml-1 font-mono text-xs text-[var(--color-neutral-600)]">
                      {{ finding.ids.join(', ') }}
                    </span>
                  </span>
                </li>
              }
            </ul>
          </details>
        }

        <div class="mt-8 grid gap-8 lg:grid-cols-[minmax(0,20rem)_minmax(0,1fr)]">
          <aside class="lg:sticky lg:top-20 lg:max-h-[calc(100vh-7rem)] lg:overflow-y-auto">
            <h2 class="text-sm font-medium text-[var(--color-neutral-400)]">Outline</h2>
            <div class="mt-3">
              @if (current.root; as root) {
                <app-tree-view [root]="root" (selected)="onSectionSelected($event)" />
              }
            </div>
          </aside>

          <article class="min-w-0 space-y-6">
            @for (section of flatSections(); track section.sectionId) {
              @if (section.paragraphIds.length > 0) {
                <section [id]="'section-' + section.sectionId">
                  <h3
                    class="text-sm font-medium tracking-wide text-[var(--color-neutral-400)] uppercase"
                  >
                    {{ section.title }}
                    @if (section.titleInferred) {
                      <span class="ml-1.5 normal-case text-[var(--color-neutral-600)]">(inferred)</span>
                    }
                  </h3>
                  @for (id of section.paragraphIds; track id) {
                    @if (paragraph(id); as p) {
                      <p class="mt-3 leading-relaxed" [attr.data-paragraph-id]="p.paragraphId">
                        @if (p.kind !== 'body') {
                          <span class="chip mr-2 align-middle">{{ p.kind }}</span>
                        }
                        {{ p.text }}
                      </p>
                    }
                  }
                </section>
              }
            }
          </article>
        </div>
      } @else if (error(); as message) {
        <p class="text-sm text-[#f0a3a3]" role="alert">{{ message }}</p>
      } @else {
        <p class="text-sm text-[var(--color-neutral-500)]">Loading…</p>
      }
    </app-shell>
  `,
  styles: `
    .chip {
      border-radius: 999px;
      border: 1px solid var(--color-divider);
      padding: 0.0625rem 0.5rem;
      font-size: 0.75rem;
      color: var(--color-neutral-500);
    }
  `,
})
export class TreePage {
  private readonly api = inject(SegmentationApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly result = signal<Result | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly runId = this.route.snapshot.paramMap.get('runId') ?? '';

  private readonly paragraphsById = computed(() => {
    const map = new Map<string, Paragraph>();
    for (const paragraph of this.result()?.paragraphs ?? []) {
      map.set(paragraph.paragraphId, paragraph);
    }
    return map;
  });

  /** Depth-first order — the order the document reads, which is what the reading pane renders. */
  protected readonly flatSections = computed(() => {
    const sections: SectionNode[] = [];
    const root = this.result()?.root;

    const walk = (node: SectionNode): void => {
      sections.push(node);
      node.children.forEach(walk);
    };

    if (root) {
      walk(root);
    }

    return sections;
  });

  protected readonly sectionCount = computed(() => this.flatSections().length);

  constructor() {
    void this.load();
  }

  protected paragraph(id: string): Paragraph | undefined {
    return this.paragraphsById().get(id);
  }

  protected onSectionSelected(section: SectionNode): void {
    document
      .getElementById(`section-${section.sectionId}`)
      ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  private async load(): Promise<void> {
    try {
      this.result.set(await this.api.getResult(this.runId));
    } catch (cause) {
      this.error.set(
        cause instanceof Error ? cause.message : 'This run has not produced a result yet.',
      );
    }
  }
}
