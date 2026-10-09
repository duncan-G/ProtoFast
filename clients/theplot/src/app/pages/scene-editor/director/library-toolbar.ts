import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  signal,
} from '@angular/core';
import { DescribeTarget } from '../../../direction/model/describe-target';
import { plural } from '../format';
import { SceneEditorStore } from '../scene-editor-store';

type Menu = 'suggest' | 'generate' | 'direct';
type Group = 'cast' | 'places' | 'props';
type Run = Group | 'library';

interface Sheet {
  target: DescribeTarget;
  written: boolean;
}

const GROUPS: { group: Group; label: string; noun: string }[] = [
  { group: 'cast', label: 'Cast', noun: 'character' },
  { group: 'places', label: 'Places', noun: 'place' },
  { group: 'props', label: 'Props', noun: 'prop' },
];

/** What the model can do for the whole story library, like the scene's toolbar does for a scene. */
@Component({
  selector: 'app-library-toolbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'dr-library-foot',
    '(document:mousedown)': 'onDocumentDown($event)',
  },
  template: `
    <button
      type="button"
      class="dr-btn"
      title="Have the model write the sheets of part of the library and cast the characters’ voices"
      [class.is-working]="running() !== null && running() !== 'library'"
      [attr.aria-expanded]="open() === 'suggest'"
      (click)="toggle('suggest')"
    >
      Suggest ▴
    </button>
    <button
      type="button"
      class="dr-btn"
      [attr.aria-expanded]="open() === 'generate'"
      (click)="toggle('generate')"
    >
      Generate ▴
    </button>
    <div class="dr-split">
      <button
        type="button"
        class="dr-split-main"
        title="The model writes every empty sheet in the library; the ones you wrote stay"
        [class.is-working]="running() === 'library'"
        [disabled]="busy() || unwritten().length === 0"
        (click)="run('library', unwritten())"
      >
        {{ running() === 'library' ? progress() : 'Direct library' }}
      </button>
      <button
        type="button"
        class="dr-split-more"
        aria-label="More ways to direct the library"
        [attr.aria-expanded]="open() === 'direct'"
        (click)="toggle('direct')"
      >
        ▴
      </button>
    </div>

    @switch (open()) {
      @case ('suggest') {
        <div class="dr-menu" role="menu">
          <span class="dr-menu-kicker">Model · sheets</span>
          @for (item of groups; track item.group) {
            <button
              type="button"
              class="dr-menu-item"
              role="menuitem"
              [class.is-working]="running() === item.group"
              [disabled]="busy() || sheets()[item.group].length === 0"
              (click)="run(item.group, sheets()[item.group])"
            >
              <span class="flex-1">{{ item.label }}</span>
              <span class="dr-menu-meta">
                {{
                  running() === item.group
                    ? progress()
                    : plural(sheets()[item.group].length, item.noun)
                }}
              </span>
            </button>
          }
        </div>
      }
      @case ('generate') {
        <div class="dr-menu" role="menu">
          <span class="dr-menu-kicker">Images · missing &amp; out of date</span>
          @for (item of groups; track item.group) {
            <button
              type="button"
              class="dr-menu-item"
              role="menuitem"
              title="Pictures each entry from its sheet; frames are kept consistent with these"
              [disabled]="unpictured()[item.group].length === 0"
              (click)="picture(unpictured()[item.group])"
            >
              <span class="flex-1">{{ item.label }}</span>
              <span class="dr-menu-meta">{{
                plural(unpictured()[item.group].length, item.noun)
              }}</span>
            </button>
          }
        </div>
      }
      @case ('direct') {
        <div class="dr-menu" role="menu">
          <button
            type="button"
            class="dr-menu-item"
            role="menuitem"
            [disabled]="busy() || all().length === 0"
            (click)="run('library', all())"
          >
            <span class="flex-1">Redo from scratch</span>
            <span class="dr-menu-meta">replaces your sheets</span>
          </button>
        </div>
      }
    }
  `,
})
export class LibraryToolbar {
  protected readonly store = inject(SceneEditorStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly groups = GROUPS;
  protected readonly plural = plural;
  protected readonly open = signal<Menu | null>(null);
  protected readonly running = signal<Run | null>(null);

  protected readonly busy = computed(() => this.store.describing() !== null);

  protected readonly sheets = computed<Record<Group, Sheet[]>>(() => ({
    cast: this.store.characters().map((c) => ({
      target: { kind: 'character', id: c.id },
      written: !!c.description,
    })),
    places: this.store.locations().map((l) => ({
      target: { kind: 'location', id: l.id },
      written: !!l.description,
    })),
    props: this.store.props().map((p) => ({
      target: { kind: 'prop', id: p.id },
      written: !!p.description,
    })),
  }));

  /** Entries without a current picture, by group. */
  protected readonly unpictured = computed<Record<Group, DescribeTarget[]>>(() => {
    const need = (targets: Sheet[]) =>
      targets
        .map((s) => s.target)
        .filter((t) => ['none', 'stale'].includes(this.store.libraryImageState(t.id)));
    const sheets = this.sheets();
    return {
      cast: need(sheets.cast),
      places: need(sheets.places),
      props: need(sheets.props),
    };
  });

  protected readonly all = computed(() => GROUPS.flatMap((g) => this.sheets()[g.group]));
  protected readonly unwritten = computed(() => this.all().filter((s) => !s.written));

  protected readonly progress = computed(() => {
    const run = this.store.describeRun();
    const at = run.indexOf(this.store.describing() ?? '');
    return at >= 0 ? `Writing ${at + 1} of ${run.length}…` : 'Writing…';
  });

  protected toggle(menu: Menu): void {
    this.open.set(this.open() === menu ? null : menu);
  }

  protected async run(run: Run, sheets: readonly Sheet[]): Promise<void> {
    this.open.set(null);
    this.running.set(run);
    try {
      await this.store.describeAll(sheets.map((s) => s.target));
    } finally {
      this.running.set(null);
    }
  }

  protected picture(targets: readonly DescribeTarget[]): void {
    this.open.set(null);
    void this.store.generateLibraryImages(targets);
  }

  protected onDocumentDown(event: MouseEvent): void {
    if (this.open() !== null && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(null);
    }
  }
}
