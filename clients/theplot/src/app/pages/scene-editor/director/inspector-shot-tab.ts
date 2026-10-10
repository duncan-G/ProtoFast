import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { extraOnScreenOf, Framing, framingOf, onScreenOf } from '../../../direction/inheritance';
import { DIRECTION_VOCABULARY, labelText } from '../../../direction/model/direction-vocabulary';
import { emptyDirection } from '../../../direction/model/scene-direction';
import { SceneEditorStore, Staging, StagingSubject } from '../scene-editor-store';

const GROUPS: { key: Framing; title: string; labels: readonly string[] }[] = [
  { key: 'shot', title: 'Shot size', labels: DIRECTION_VOCABULARY.shots },
  { key: 'angle', title: 'Angle', labels: DIRECTION_VOCABULARY.angles },
  { key: 'movement', title: 'Movement', labels: DIRECTION_VOCABULARY.movements },
];

/** The camera for the selected frame, or the scene's default for every frame. */
@Component({
  selector: 'app-inspector-shot-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dr-tab-body' },
  template: `
    @for (group of groups(); track group.key) {
      <div class="dr-field">
        <div class="dr-field-head">
          <span class="dr-kicker">{{ group.title }}</span>
          <span class="flex-1"></span>
          @if (frame()) {
            @if (group.overridden) {
              <span class="dr-override"
                >override ·
                <button type="button" class="dr-link is-quiet" (click)="reset(group.key)">
                  reset
                </button></span
              >
            } @else {
              <span class="dr-inherited">from scene</span>
            }
          }
        </div>
        <div class="dr-pills" role="radiogroup" [attr.aria-label]="group.title">
          @for (label of group.labels; track label) {
            <button
              type="button"
              class="dr-pill"
              role="radio"
              [attr.aria-checked]="label === group.value"
              (click)="store.setFraming(group.key, label, frameKey())"
            >
              {{ text(label) }}
            </button>
          }
        </div>
      </div>
    }
    @if (frame(); as frame) {
      <div class="dr-field">
        <span class="dr-kicker">Camera position</span>
        <textarea
          class="dr-input"
          rows="2"
          placeholder="Where the camera stands and what it looks toward. e.g. inside the corridor past the double doors, looking back at them"
          maxlength="2000"
          [value]="frame.camera"
          (change)="store.updateFrame(frame.firstElementId, { camera: $any($event.target).value })"
        ></textarea>
      </div>
      <div class="dr-field">
        <div class="dr-field-head">
          <span class="dr-kicker">Staging</span>
          <span class="flex-1"></span>
          <span class="dr-hint">as the viewer sees the picture</span>
        </div>
        @for (row of staging(); track row.who.kind + row.who.id) {
          <div class="dr-stage">
            <span class="dr-stage-name">{{ row.name }}</span>
            <div class="dr-stage-grid">
              @for (pick of picks; track pick.field) {
                <label class="dr-stage-cell">
                  <span class="dr-stage-label">{{ pick.title }}</span>
                  <select
                    class="dr-input"
                    [attr.aria-label]="row.name + ' ' + pick.title"
                    (change)="stage(row.who, pick.field, $any($event.target).value)"
                  >
                    <option value="" [selected]="!row.staging[pick.field]">—</option>
                    @for (label of pick.labels; track label) {
                      <option [value]="label" [selected]="label === row.staging[pick.field]">
                        {{ text(label) }}
                      </option>
                    }
                  </select>
                </label>
              }
            </div>
            <label class="dr-stage-cell">
              <span class="dr-stage-label">Looking at</span>
              <input
                class="dr-input"
                maxlength="255"
                placeholder="Who or what, e.g. the nurse"
                [value]="row.staging.looking"
                (change)="stage(row.who, 'looking', $any($event.target).value)"
              />
            </label>
            <label class="dr-stage-cell">
              <span class="dr-stage-label">{{
                row.who.kind === 'extra' ? 'Which of them, and pose' : 'Pose'
              }}</span>
              <input
                class="dr-input"
                maxlength="255"
                [placeholder]="
                  row.who.kind === 'extra'
                    ? 'e.g. the nurse nearest the camera, mid-step'
                    : 'e.g. a palm on the swinging door'
                "
                [value]="row.staging.pose"
                (change)="stage(row.who, 'pose', $any($event.target).value)"
              />
            </label>
          </div>
        } @empty {
          <span class="dr-hint">Nobody is on screen in this frame.</span>
        }
      </div>
      <div class="dr-field">
        <span class="dr-kicker">What’s visible</span>
        <textarea
          class="dr-input"
          rows="8"
          placeholder="Who is in the frame and what each is doing, how they stand to each other and the place, and what else is happening. e.g. Mara leans across the bar toward Bolt, who stands stiffly by the door; rain streaks the window behind them."
          maxlength="2000"
          [value]="frame.beat"
          (change)="store.updateFrame(frame.firstElementId, { beat: $any($event.target).value })"
        ></textarea>
        <div class="dr-hold">
          <span>Hold</span>
          <input
            class="dr-input dr-input-mono"
            type="number"
            min="1"
            step="250"
            placeholder="auto"
            aria-label="Hold, in milliseconds"
            [value]="frame.holdMs ?? ''"
            (change)="
              store.updateFrame(frame.firstElementId, {
                holdMs: +$any($event.target).value || null,
              })
            "
          />
          <span class="dr-hint">ms, for frames without lines</span>
        </div>
      </div>
    } @else {
      <div class="dr-field">
        <span class="dr-kicker">Screen direction</span>
        @for (row of travels(); track row.id) {
          <div class="dr-stage-travel">
            <span class="flex-1">{{ row.name }}</span>
            <select
              class="dr-input"
              [attr.aria-label]="row.name + ' screen direction'"
              (change)="store.setTravel(row.id, $any($event.target).value)"
            >
              <option value="" [selected]="!row.travel">Doesn’t cross</option>
              @for (label of travelLabels; track label) {
                <option [value]="label" [selected]="label === row.travel">{{ text(label) }}</option>
              }
            </select>
          </div>
        } @empty {
          <span class="dr-hint">Nobody speaks or is mentioned in this scene yet.</span>
        }
        <span class="dr-hint"
          >The way each moves across the screen in every frame, so a cut never reverses them.</span
        >
      </div>
    }
  `,
})
export class InspectorShotTab {
  protected readonly store = inject(SceneEditorStore);

  protected readonly frame = computed(() =>
    this.store.inspectorScope() === 'frame' ? (this.store.selectedFrame()?.frame ?? null) : null,
  );
  protected readonly frameKey = computed(() => this.frame()?.firstElementId ?? null);

  protected readonly groups = computed(() => {
    const direction = this.store.direction() ?? emptyDirection('');
    return GROUPS.map((group) => ({ ...group, ...framingOf(direction, this.frame(), group.key) }));
  });

  protected readonly picks: {
    field: 'screen' | 'facing' | 'moving';
    title: string;
    labels: readonly string[];
  }[] = [
    {
      field: 'screen',
      title: 'Side',
      labels: DIRECTION_VOCABULARY.screenSides.filter((s) => s !== 'off'),
    },
    { field: 'facing', title: 'Facing', labels: DIRECTION_VOCABULARY.facings },
    { field: 'moving', title: 'Moving', labels: DIRECTION_VOCABULARY.movings },
  ];
  protected readonly travelLabels = DIRECTION_VOCABULARY.travels;

  /** Everyone the frame shows, with what it says about where they are and which way they go. */
  protected readonly staging = computed(() => {
    const frame = this.frame();
    const direction = this.store.direction();
    if (!frame || !direction) {
      return [];
    }
    const blank: Staging = { screen: '', facing: '', moving: '', looking: '', pose: '' };
    const cast = this.store
      .cast()
      .filter((c) => onScreenOf(direction, frame, c.id).value)
      .map((c) => ({
        who: { kind: 'character', id: c.id } as StagingSubject,
        name: c.name,
        staging: frame.placements.find((p) => p.characterId === c.id) ?? blank,
      }));
    const extras = this.store
      .extrasInScene()
      .filter((e) => extraOnScreenOf(direction, frame, e.id).value)
      .map((e) => ({
        who: { kind: 'extra', id: e.id } as StagingSubject,
        name: e.name,
        staging: frame.extras.find((x) => x.extraId === e.id) ?? blank,
      }));
    return [...cast, ...extras];
  });

  protected readonly travels = computed(() =>
    this.store.cast().map((c) => ({
      id: c.id,
      name: c.name,
      travel: this.store.appearanceByCharacter().get(c.id)?.travel ?? '',
    })),
  );

  protected stage(who: StagingSubject, field: keyof Staging, value: string): void {
    const frameKey = this.frameKey();
    if (frameKey) {
      this.store.setStaging(who, { [field]: value.trim() }, frameKey);
    }
  }

  protected text(label: string): string {
    return labelText(label);
  }

  protected reset(key: Framing): void {
    const frameKey = this.frameKey();
    if (frameKey) {
      this.store.updateFrame(frameKey, { [key]: null });
    }
  }
}
