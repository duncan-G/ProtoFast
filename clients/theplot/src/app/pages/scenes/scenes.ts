import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AppShell } from '../../shared/app-shell';
import { TreeView } from '../../shared/tree-view';
import { SegmentationApi } from '../../segmentation/segmentation-api';
import { sceneCards, type SceneCardGroup } from '../../segmentation/scenes';
import type { SceneResult, SectionNode } from '../../../lib/gen/segmentation_pb';

/**
 * The scene stream: what the document *is*, as opposed to how it is filed.
 *
 * The tree page answers "where does this text sit"; this page answers "what happens, where, and
 * who is there". They are deliberately two views of one frozen record — a section is a constraint
 * on where a scene may be cut, not the thing a reader came for (scene plan §2).
 *
 * Nothing here reads a paragraph. The server resolved every span's text and every persona's name
 * before sending it, which is what K1 asks for — a scene renders from its own record — and is why
 * this page makes one call and no joins.
 */
@Component({
  selector: 'app-scenes-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AppShell, TreeView, RouterLink, DecimalPipe],
  template: `
    <app-shell>
      @if (result(); as current) {
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div class="min-w-0">
            <h1 class="truncate text-2xl font-semibold tracking-tight">
              {{ current.root?.title }}
            </h1>
            <p
              class="mt-1 flex flex-wrap items-center gap-x-3 text-sm text-[var(--color-neutral-500)]"
            >
              <span>
                {{ current.totalScenes }} {{ current.totalScenes === 1 ? 'scene' : 'scenes' }}
              </span>
              <span>{{ itemCount() }} items</span>
              <span>{{ current.personas.length }} in the cast</span>
              <span class="font-mono text-xs" title="The content hash this structure was frozen at">
                {{ current.treeHash.slice(0, 12) }}
              </span>
            </p>
          </div>
          <div class="flex shrink-0 items-center gap-2">
            <a [routerLink]="['/app/runs', runId, 'tree']" class="btn btn-secondary">The tree</a>
            <a [routerLink]="['/app/runs', runId]" class="btn btn-secondary">Run details</a>
          </div>
        </div>

        @if (current.scenes.length === 0) {
          <!--
            An empty stream is not an error, and the two reasons for it need different answers: a
            re-run fixes a run that finished before the scene phases existed, and nothing fixes a
            document that had no displayable text.
          -->
          <div
            class="mt-10 rounded-[var(--radius-lg)] border border-[var(--color-divider)] bg-[var(--color-surface)] p-5"
          >
            <p class="text-sm">This run has no scenes.</p>
            <p class="mt-2 text-sm text-[var(--color-neutral-500)]">
              Either it finished before the scene phases existed, or the document had nothing
              displayable to cut — front matter and navigation are excluded from the scene stream.
              Re-running from <span class="font-medium">Items</span> will cut scenes for a document
              that has them.
            </p>
            <a [routerLink]="['/app/runs', runId]" class="btn btn-secondary mt-4 inline-flex">
              Open the run
            </a>
          </div>
        } @else {
          <div class="mt-8 grid gap-8 lg:grid-cols-[minmax(0,18rem)_minmax(0,1fr)]">
            <aside class="lg:sticky lg:top-20 lg:max-h-[calc(100vh-7rem)] lg:overflow-y-auto">
              <h2 class="text-sm font-medium text-[var(--color-neutral-400)]">Outline</h2>
              <div class="mt-3">
                @if (current.root; as root) {
                  <app-tree-view [root]="root" (selected)="onSectionSelected($event)" />
                }
              </div>

              @if (current.personas.length > 0) {
                <h2 class="mt-8 text-sm font-medium text-[var(--color-neutral-400)]">Cast</h2>
                <ul class="mt-3 space-y-1 text-sm">
                  @for (persona of current.personas; track persona.personaId) {
                    <li class="flex items-baseline gap-2">
                      <span class="min-w-0 break-words">{{ persona.canonicalName }}</span>
                      @if (persona.kind === 'group') {
                        <span class="chip shrink-0">group</span>
                      }
                      @if (persona.scope === 'scene_local') {
                        <span class="chip shrink-0" title="Named in one scene and not beyond it">
                          local
                        </span>
                      }
                    </li>
                  }
                </ul>
              }
            </aside>

            <div class="min-w-0 space-y-10">
              @for (group of groups(); track group.sectionId) {
                <section [id]="'section-' + group.sectionId">
                  <h2
                    class="text-sm font-medium tracking-wide text-[var(--color-neutral-400)] uppercase"
                  >
                    {{ group.path }}
                  </h2>

                  <div class="mt-4 space-y-6">
                    @for (scene of group.scenes; track scene.sceneId) {
                      <article
                        class="rounded-[var(--radius-lg)] border border-[var(--color-divider)] bg-[var(--color-surface)] p-5"
                        [id]="'scene-' + scene.sceneId"
                      >
                        <header class="flex flex-wrap items-baseline justify-between gap-2">
                          <h3 class="min-w-0 text-base font-medium">
                            {{ scene.label }}
                            @if (scene.titleInferred) {
                              <span
                                class="ml-1.5 text-xs font-normal text-[var(--color-neutral-600)]"
                                title="ThePlot named this scene; the document did not"
                              >
                                inferred
                              </span>
                            }
                          </h3>
                          <div class="flex shrink-0 flex-wrap items-center gap-1.5">
                            @if (scene.situation.mode) {
                              <span class="chip" [title]="scene.modeHint">
                                {{ scene.situation.mode }}
                              </span>
                            }
                            @for (flag of scene.flags; track flag.kind) {
                              <span class="chip chip-warn" [title]="flag.message">
                                {{ flag.kind }}
                              </span>
                            }
                          </div>
                        </header>

                        <!--
                          The coordinates, always all of them. "no place" is a claim about the scene
                          rather than an empty field, so it is shown as plainly as a named place is.
                        -->
                        <dl
                          class="mt-3 grid gap-x-6 gap-y-1 text-xs sm:grid-cols-[auto_minmax(0,1fr)]"
                        >
                          <dt class="text-[var(--color-neutral-600)]">Where</dt>
                          <dd class="text-[var(--color-neutral-400)]">
                            {{ scene.situation.place }}
                            @if (scene.situation.placeInherited) {
                              <span
                                class="ml-1 text-[var(--color-neutral-600)]"
                                title="Carried from the scene before it; this scene did not say where it is"
                              >
                                (carried)
                              </span>
                            }
                          </dd>

                          @if (scene.situation.time) {
                            <dt class="text-[var(--color-neutral-600)]">When</dt>
                            <dd class="text-[var(--color-neutral-400)]">
                              {{ scene.situation.time }}
                            </dd>
                          }

                          @if (scene.situation.cast.length > 0) {
                            <dt class="text-[var(--color-neutral-600)]">Who</dt>
                            <dd
                              class="flex flex-wrap gap-x-2 gap-y-1 text-[var(--color-neutral-400)]"
                            >
                              @for (member of scene.situation.cast; track member.personaId) {
                                <span>
                                  {{ member.name }}
                                  <span class="text-[var(--color-neutral-600)]">
                                    ({{ member.role }})
                                  </span>
                                </span>
                              }
                            </dd>
                          }

                          @if (scene.situation.subject) {
                            <dt class="text-[var(--color-neutral-600)]">About</dt>
                            <dd class="text-[var(--color-neutral-400)]">
                              {{ scene.situation.subject }}
                            </dd>
                          }
                        </dl>

                        @if (scene.links.length > 0) {
                          <ul class="mt-3 flex flex-wrap gap-1.5">
                            @for (link of scene.links; track $index) {
                              <li>
                                <button
                                  type="button"
                                  class="chip chip-accent hover:text-[var(--color-accent-200)]"
                                  [disabled]="!link.targetSceneId"
                                  (click)="onLinkFollowed(link.targetSceneId)"
                                >
                                  {{ link.label }}
                                  @if (link.showsConfidence) {
                                    <span class="text-[var(--color-neutral-500)]">
                                      {{ link.confidence * 100 | number: '1.0-0' }}%
                                    </span>
                                  }
                                </button>
                              </li>
                            }
                          </ul>
                        }

                        <div class="mt-4 space-y-2">
                          @for (item of scene.items; track item.itemId) {
                            @switch (item.kind) {
                              @case ('speech') {
                                <p class="leading-relaxed" [attr.data-item-id]="item.itemId">
                                  @if (item.speaker) {
                                    <span class="font-medium text-[var(--color-accent-300)]">
                                      {{ item.speaker }}
                                    </span>
                                    <span class="text-[var(--color-neutral-600)]">:</span>
                                  }
                                  <span [class.italic]="item.toAudience">{{ item.text }}</span>
                                  @if (item.generated) {
                                    <span class="staged" [title]="STAGED_HINT">staged</span>
                                  }
                                </p>
                              }
                              @case ('transition') {
                                <p
                                  class="py-1 text-center text-xs tracking-widest text-[var(--color-neutral-500)] uppercase"
                                  [attr.data-item-id]="item.itemId"
                                >
                                  {{ item.text }}
                                </p>
                              }
                              @case ('exhibit') {
                                <pre
                                  class="overflow-x-auto rounded-[var(--radius-sm)] border border-[var(--color-divider)] p-3 font-mono text-xs text-[var(--color-neutral-400)]"
                                  [attr.data-item-id]="item.itemId"
                                  >{{ item.text }}</pre>
                              }
                              @default {
                                <p
                                  class="leading-relaxed"
                                  [class.description]="item.kind === 'description'"
                                  [attr.data-item-id]="item.itemId"
                                >
                                  {{ item.text }}
                                  @if (item.generated) {
                                    <span class="staged" [title]="STAGED_HINT">staged</span>
                                  }
                                </p>
                              }
                            }
                          }
                        </div>
                      </article>
                    }
                  </div>
                </section>
              }
            </div>
          </div>
        }
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
    .chip-warn {
      border-color: #7a3a3a;
      color: #f0a3a3;
    }
    .chip-accent {
      border-color: var(--color-accent-600);
      color: var(--color-accent-300);
    }
    .chip:disabled {
      opacity: 0.6;
    }
    /* Description sets the frame rather than animating it, and reads as a lower register. */
    .description {
      color: var(--color-neutral-400);
    }
    .staged {
      margin-left: 0.25rem;
      vertical-align: middle;
      font-size: 0.625rem;
      color: var(--color-neutral-600);
    }
  `,
})
export class ScenesPage {
  private readonly api = inject(SegmentationApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly result = signal<SceneResult | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly runId = this.route.snapshot.paramMap.get('runId') ?? '';

  /** Said once rather than at both call sites, because it is the same claim in both. */
  protected readonly STAGED_HINT =
    "Staging text ThePlot wrote; the document's own words are the span it came from";

  protected readonly groups = computed<SceneCardGroup[]>(() => {
    const current = this.result();
    return current ? sceneCards(current.scenes, current.root) : [];
  });

  protected readonly itemCount = computed(() =>
    (this.result()?.scenes ?? []).reduce((total, scene) => total + scene.items.length, 0),
  );

  constructor() {
    void this.load();
  }

  protected onSectionSelected(section: SectionNode): void {
    this.scrollTo(`section-${section.sectionId}`);
  }

  /**
   * A link jumps to its target. Scrolling rather than following an `href` fragment keeps the
   * router out of it — a flashback is a place in this page, not a route — and a link whose target
   * lies outside a section-filtered view has nothing to jump to and is disabled instead.
   */
  protected onLinkFollowed(sceneId: string): void {
    if (sceneId) {
      this.scrollTo(`scene-${sceneId}`);
    }
  }

  private scrollTo(id: string): void {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  private async load(): Promise<void> {
    try {
      this.result.set(await this.api.getScenes(this.runId));
    } catch (cause) {
      this.error.set(
        cause instanceof Error ? cause.message : 'This run has not produced a result yet.',
      );
    }
  }
}
