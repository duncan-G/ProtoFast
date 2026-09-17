import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import type { SectionNode } from '../../lib/gen/segmentation_pb';

/** One visible row of the outline. */
export interface TreeRow {
  readonly sectionId: string;
  readonly title: string;
  readonly inferred: boolean;
  readonly depth: number;
  readonly paragraphCount: number;
  readonly hasChildren: boolean;
  readonly expanded: boolean;
  readonly node: SectionNode;
}

/**
 * The section tree, rendered as a flattened outline.
 *
 * Flattening rather than recursing is deliberate. A recursive standalone component has to import
 * itself, which is a temporal-dead-zone hazard, and a deep tree becomes a deep component
 * hierarchy that Angular re-checks on every change. A flat list of rows with a depth number
 * renders a four-hundred-section document in one pass.
 *
 * Inferred titles are marked, which is a product decision as much as a design one: a title the
 * pipeline invented is a different kind of claim from one the document made, and a reader
 * deciding whether to trust the structure needs to tell them apart.
 */
@Component({
  selector: 'app-tree-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="text-sm">
      @for (row of rows(); track row.sectionId) {
        <li
          class="flex items-baseline gap-2 rounded-[var(--radius-sm)] py-0.5"
          [class.bg-[var(--color-surface)]]="row.sectionId === selectedId()"
          [style.padding-left.rem]="row.depth * 0.9"
        >
          @if (row.hasChildren) {
            <button
              type="button"
              class="w-4 shrink-0 text-xs text-[var(--color-neutral-500)] hover:text-[var(--color-text)]"
              [attr.aria-expanded]="row.expanded"
              [attr.aria-label]="(row.expanded ? 'Collapse ' : 'Expand ') + row.title"
              (click)="toggle(row.sectionId)"
            >
              {{ row.expanded ? '▾' : '▸' }}
            </button>
          } @else {
            <span class="w-4 shrink-0" aria-hidden="true"></span>
          }

          <button
            type="button"
            class="min-w-0 flex-1 text-left hover:text-[var(--color-accent-300)]"
            [class.font-medium]="row.depth <= 1"
            (click)="select(row)"
          >
            <span class="break-words">{{ row.title }}</span>
            @if (row.inferred) {
              <span
                class="ml-1.5 rounded-full border border-[var(--color-divider)] px-1.5 py-px align-middle text-[0.625rem] text-[var(--color-neutral-500)]"
                title="ThePlot named this section; the document did not"
              >
                inferred
              </span>
            }
            @if (row.paragraphCount > 0) {
              <span class="ml-1.5 text-xs text-[var(--color-neutral-600)]">
                {{ row.paragraphCount }}
              </span>
            }
          </button>
        </li>
      }
    </ul>
  `,
})
export class TreeView {
  readonly root = input.required<SectionNode>();
  readonly selected = output<SectionNode>();

  protected readonly selectedId = signal<string | null>(null);

  /**
   * Sections collapsed by the reader. Tracking the collapsed set rather than the expanded one
   * means a freshly loaded document starts fully open, which is what someone checking a structure
   * wants — and it survives the tree changing under it, because an id that vanishes just stops
   * matching.
   */
  private readonly collapsed = signal<ReadonlySet<string>>(new Set());

  protected readonly rows = computed<TreeRow[]>(() => {
    const collapsed = this.collapsed();
    const rows: TreeRow[] = [];

    const walk = (node: SectionNode, depth: number): void => {
      const expanded = !collapsed.has(node.sectionId);

      rows.push({
        sectionId: node.sectionId,
        title: node.title,
        inferred: node.titleInferred,
        depth,
        paragraphCount: node.paragraphIds.length,
        hasChildren: node.children.length > 0,
        expanded,
        node,
      });

      if (expanded) {
        for (const child of node.children) {
          walk(child, depth + 1);
        }
      }
    };

    walk(this.root(), 0);
    return rows;
  });

  protected toggle(sectionId: string): void {
    this.collapsed.update((current) => {
      const next = new Set(current);
      if (!next.delete(sectionId)) {
        next.add(sectionId);
      }
      return next;
    });
  }

  protected select(row: TreeRow): void {
    this.selectedId.set(row.sectionId);
    this.selected.emit(row.node);
  }
}
