import { computed, inject, Injectable, OnDestroy, signal } from '@angular/core';
import { Router } from '@angular/router';
import { DirectionApi } from '../../direction/direction-api';
import { directionChanges, isSpoken } from '../../direction/direction-changes';
import {
  ElementOrder,
  extraOnScreenOf,
  Framing,
  inViewOf,
  joinChanges,
  lookAt,
  onScreenOf,
  withPlacement,
} from '../../direction/inheritance';
import { Cue } from '../../direction/model/cue';
import { DescribeTarget } from '../../direction/model/describe-target';
import { covers, DirectTarget } from '../../direction/model/direct-target';
import { DIRECTION_VOCABULARY } from '../../direction/model/direction-vocabulary';
import { Frame } from '../../direction/model/frame';
import { LibraryImage } from '../../direction/model/library-image';
import { LookChange } from '../../direction/model/look-change';
import { blankAppearance, SceneAppearance } from '../../direction/model/scene-appearance';
import { emptyDirection, isDirected, SceneDirection } from '../../direction/model/scene-direction';
import { blankPropState, ScenePropState } from '../../direction/model/scene-prop-state';
import { blankSceneExtra } from '../../direction/model/scene-extra';
import { SpeechVoice } from '../../direction/model/speech-voice';
import { VoiceQuery } from '../../direction/model/voice-query';
import { ScenePlayer } from '../../direction/scene-player';
import {
  cover,
  headingAt,
  mergeUp,
  frameSpans,
  FrameSpan,
  removeFrame,
  renumber,
  spanAt,
  splitAt,
} from '../../direction/frames';
import { insertTag, reanchor, removeTag, stripTags } from '../../direction/performance';
import { describeError } from '../../documents/document-api';
import { forgetInElement, renameInElement } from '../../stories/element-references';
import { labelProblem, nameProblem, sameName } from '../../stories/library-names';
import { insertReference, tagReference } from '../../stories/mentions';
import { Character } from '../../stories/model/character';
import { CharacterKind } from '../../stories/model/character-kind';
import { DEFAULT_VOCABULARY } from '../../stories/model/default-vocabulary';
import { Extra } from '../../stories/model/extra';
import { LibraryTarget } from '../../stories/model/library-target';
import { Location } from '../../stories/model/location';
import { LocationSetting } from '../../stories/model/location-setting';
import { MentionedText } from '../../stories/model/mentioned-text';
import { Prop } from '../../stories/model/prop';
import { Referable } from '../../stories/model/referable';
import { ReferenceTarget } from '../../stories/model/reference-target';
import { Scene } from '../../stories/model/scene';
import { SceneElement } from '../../stories/model/scene-element';
import { SceneElementType } from '../../stories/model/scene-element-type';
import { Story } from '../../stories/model/story';
import { StoryVocabulary } from '../../stories/model/story-vocabulary';
import { EMPTY_VOICE } from '../../stories/model/voice-profile';
import { Narrator } from '../../stories/model/narrator';
import { isBeat, moveBlock, swapElement } from '../../stories/scene-blocks';
import { StoryApi } from '../../stories/story-api';
import { CaretRequest } from './caret-request';
import { nextHue } from './format';
import { LibraryTab } from './library-tab';
import { ScenePlace } from './scene-place';
import { SceneRow } from './scene-row';
import { SceneStats } from './scene-stats';
import { EditorMode } from './editor-mode';
import { AudioState } from './audio-state';
import { ImageState } from './image-state';
import { DirectorLayout } from './director-layout';
import { DirectorLibraryTab } from './director-library-tab';
import { InspectorScope } from './inspector-scope';
import { InspectorTab } from './inspector-tab';

const SAVE_DELAY_MS = 400;
const UNTITLED = 'Untitled scene';
/** The API caps a narrator's name and voice description at this. */
const NARRATOR_TEXT_MAX = 1000;

/**
 * Scene edits save shortly after typing stops; library and outline changes save at once. In
 * director mode the scene and its director layer save together, so a changed line lands with
 * its cue.
 */
@Injectable()
export class SceneEditorStore implements OnDestroy {
  private readonly api = inject(StoryApi);
  private readonly directionApi = inject(DirectionApi);
  private readonly router = inject(Router);
  readonly player = inject(ScenePlayer);

  readonly story = signal<Story | null>(null);
  readonly scene = signal<Scene | null>(null);
  readonly direction = signal<SceneDirection | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');

  readonly mode = signal<EditorMode>('story');
  readonly directorLayout = signal<DirectorLayout>('script');
  readonly inspectorScope = signal<InspectorScope>('frame');
  readonly inspectorTab = signal<InspectorTab>('cast');
  readonly directorLibraryTab = signal<DirectorLibraryTab>('cast');
  /** The library card that is open, by entry id. */
  readonly directorLibraryEntry = signal<string | null>(null);
  /** The first element id of the selected frame: frames are known by their place, not their id. */
  readonly selectedFrameKey = signal<string | null>(null);
  /** What the model is working on. */
  readonly directing = signal<readonly DirectTarget[]>([]);
  /** The library entry the model is describing. */
  readonly describing = signal<string | null>(null);
  /** The entry ids of the describe run under way, in order. */
  readonly describeRun = signal<readonly string[]>([]);
  /** Element ids whose lines are being spoken; 'scene' while a whole-scene pass runs. */
  readonly generating = signal<ReadonlySet<string>>(new Set());
  /** Lines queued or being spoken, by element id. */
  readonly generatingLines = signal<ReadonlySet<string>>(new Set());
  /** The character whose voice is being cast. */
  readonly castingVoice = signal<string | null>(null);
  /** The story's library pictures, by entry id. */
  readonly libraryImages = signal<ReadonlyMap<string, LibraryImage>>(new Map());
  /** Frame ids and library entry ids queued or being pictured. */
  readonly picturing = signal<ReadonlySet<string>>(new Set());

  readonly editingId = signal<string | null>(null);
  readonly libraryTab = signal<LibraryTab>('characters');
  /** Only has an effect on phones, where the library is a drawer. */
  readonly libraryOpen = signal(false);
  /** Both modes share it, from tablets up; phones ignore it. */
  readonly libraryCollapsed = signal(false);
  /** The entry the library scrolls to as it unfolds from the rail. */
  readonly libraryEntry = signal<string | null>(null);
  /** For the library's "@ Insert", which tags the selection when there is one. */
  readonly caret = signal<number | null>(null);
  readonly selectionEnd = signal<number | null>(null);
  readonly caretRequest = signal<CaretRequest | null>(null);
  readonly titleRequest = signal(false);

  private saveTimer: ReturnType<typeof setTimeout> | null = null;
  private saving: Promise<void> = Promise.resolve();
  private sceneDirty = false;
  private directionDirty = false;
  /** Bumped on every direction edit, so a save's reply is only adopted when nothing changed since. */
  private directionVersion = 0;

  readonly characters = computed(() => this.story()?.characters ?? []);
  readonly locations = computed(() => this.story()?.locations ?? []);
  readonly props = computed(() => this.story()?.props ?? []);
  readonly characterById = computed(() => new Map(this.characters().map((c) => [c.id, c])));
  readonly locationById = computed(() => new Map(this.locations().map((l) => [l.id, l])));
  readonly propById = computed(() => new Map(this.props().map((p) => [p.id, p])));
  readonly extras = computed(() => this.story()?.extras ?? []);
  readonly extraById = computed(() => new Map(this.extras().map((e) => [e.id, e])));

  private readonly vocabulary = computed<StoryVocabulary>(
    () =>
      this.story()?.vocabulary ?? {
        timesOfDay: [],
        transitions: [],
        extensions: [],
        deliveryTags: [],
        characterKinds: [],
      },
  );
  readonly deliveryTags = computed(() => [
    ...DIRECTION_VOCABULARY.deliveryTags,
    ...this.vocabulary().deliveryTags,
  ]);
  readonly timesOfDay = computed(() => [
    ...DEFAULT_VOCABULARY.timesOfDay,
    ...this.vocabulary().timesOfDay,
  ]);
  readonly transitions = computed(() => [
    ...DEFAULT_VOCABULARY.transitions,
    ...this.vocabulary().transitions,
  ]);
  readonly extensions = computed(() => [
    ...DEFAULT_VOCABULARY.extensions,
    ...this.vocabulary().extensions,
  ]);
  readonly kinds = computed<CharacterKind[]>(() => [
    ...DEFAULT_VOCABULARY.characterKinds,
    ...this.vocabulary().characterKinds,
  ]);

  /** In `REFERENCE_ORDER`, as the autocomplete lists them. */
  readonly referables = computed<Referable[]>(() => [
    ...this.characters().map((c) => ({
      kind: 'character' as const,
      id: c.id,
      name: c.name,
    })),
    ...this.locations().map((l) => ({
      kind: 'location' as const,
      id: l.id,
      name: l.name,
    })),
    ...this.props().map((p) => ({
      kind: 'prop' as const,
      id: p.id,
      name: p.name,
    })),
  ]);

  readonly elements = computed(() => this.scene()?.elements ?? []);

  readonly rows = computed<SceneRow[]>(() => {
    const locations = this.locationById();
    let heading: SceneRow | null = null;
    let hue: number | null = null;
    return this.elements().map((element, index) => {
      const row: SceneRow = { element, index, hue, beats: 0 };
      if (element.type === 'Heading') {
        hue = element.locationId ? (locations.get(element.locationId)?.hue ?? null) : null;
        row.hue = hue;
        heading = row;
      } else if (element.type !== 'Transition' && heading) {
        heading.beats++;
      }
      return row;
    });
  });

  readonly stats = computed<SceneStats>(() => {
    const stats: SceneStats = {
      lines: new Map(),
      mentions: new Map(),
      headings: new Map(),
    };
    const bump = (map: Map<string, number>, id: string | null) => {
      if (id) {
        map.set(id, (map.get(id) ?? 0) + 1);
      }
    };
    for (const element of this.elements()) {
      if (element.type === 'Dialogue') {
        bump(stats.lines, element.speakerId);
      }
      if (element.type === 'Heading') {
        bump(stats.headings, element.locationId);
      }
      for (const mention of element.mentions) {
        bump(stats.mentions, mention.characterId ?? mention.locationId ?? mention.propId);
      }
    }
    return stats;
  });

  // ─── the director layer ────────────────────────────────────────────────

  readonly directed = computed(() => {
    const direction = this.direction();
    return direction !== null && isDirected(direction);
  });
  readonly cueByElement = computed(
    () => new Map((this.direction()?.cues ?? []).map((c) => [c.elementId, c])),
  );
  readonly frameSpans = computed<FrameSpan[]>(() =>
    frameSpans(this.direction()?.frames ?? [], this.elements()),
  );
  /** The frame the inspector edits: the one picked, else the first. */
  readonly selectedFrame = computed<FrameSpan | null>(() => {
    const key = this.selectedFrameKey();
    const spans = this.frameSpans();
    return spans.find((s) => s.frame.firstElementId === key) ?? spans[0] ?? null;
  });
  readonly selectedFrameNumber = computed(() => {
    const span = this.selectedFrame();
    return span ? this.frameSpans().indexOf(span) + 1 : 0;
  });
  readonly locationStateByLocation = computed(
    () => new Map((this.direction()?.locationStates ?? []).map((l) => [l.locationId, l])),
  );
  readonly appearanceByCharacter = computed(
    () => new Map((this.direction()?.appearances ?? []).map((a) => [a.characterId, a])),
  );
  readonly propStateByProp = computed(
    () => new Map((this.direction()?.propStates ?? []).map((p) => [p.propId, p])),
  );
  readonly elementOrder = computed<ElementOrder>(
    () => new Map(this.elements().map((e, i) => [e.id, i])),
  );
  /** Characters who speak, are mentioned or have a look in the scene, in library order. */
  readonly cast = computed(() => {
    const ids = new Set<string>([
      ...(this.direction()?.appearances.map((a) => a.characterId) ?? []),
      ...(this.direction()?.lookChanges.map((c) => c.characterId) ?? []),
    ]);
    for (const element of this.elements()) {
      if (element.type === 'Dialogue' && element.speakerId) {
        ids.add(element.speakerId);
      }
      for (const mention of element.mentions) {
        if (mention.characterId) {
          ids.add(mention.characterId);
        }
      }
    }
    return this.characters().filter((c) => ids.has(c.id));
  });
  readonly propsInScene = computed(() => {
    const direction = this.direction();
    const ids = new Set<string | null>([
      ...(direction?.propStates.map((p) => p.propId) ?? []),
      ...(direction?.appearances.flatMap((a) => a.holding) ?? []),
      ...(direction?.lookChanges.flatMap((c) => c.holding) ?? []),
    ]);
    for (const element of this.elements()) {
      element.mentions.forEach((m) => ids.add(m.propId));
    }
    return this.props().filter((p) => ids.has(p.id));
  });
  /** Extras are in a scene only through the director layer, never the text. */
  readonly extrasInScene = computed(() => {
    const ids = new Set(this.direction()?.extras.map((e) => e.extraId) ?? []);
    return this.extras().filter((e) => ids.has(e.id));
  });
  /** Where the scene's headings put it in scene order, then places only the director layer names. */
  readonly locationsInScene = computed(() => {
    const ids = [
      ...new Set([
        ...this.elements().flatMap((e) =>
          e.type === 'Heading' && e.locationId ? [e.locationId] : [],
        ),
        ...(this.direction()?.locationStates.map((l) => l.locationId) ?? []),
      ]),
    ];
    return ids.flatMap((id) => this.locationById().get(id) ?? []);
  });
  readonly narrators = computed(() => this.story()?.narrators ?? []);
  /** Lines read aloud, in scene order. */
  readonly spokenElements = computed(() => this.elements().filter((e) => isSpoken(e.type)));
  readonly audioStates = computed(() => {
    const generating = this.generatingLines();
    const cues = this.cueByElement();
    return new Map<string, AudioState>(
      this.spokenElements().map((e) => {
        const audio = cues.get(e.id)?.audio;
        const state: AudioState = generating.has(e.id)
          ? 'generating'
          : !audio
            ? 'none'
            : audio.stale
              ? 'stale'
              : 'ready';
        return [e.id, state];
      }),
    );
  });
  /** Lines without audio, or whose audio has fallen behind the performance or the voice. */
  readonly linesNeedingAudio = computed(() =>
    this.spokenElements()
      .map((e) => e.id)
      .filter(
        (id) => this.audioStates().get(id) === 'none' || this.audioStates().get(id) === 'stale',
      ),
  );

  /** Each frame's picture, by the frame's first element id. */
  readonly frameImageStates = computed(() => {
    const picturing = this.picturing();
    return new Map<string, ImageState>(
      (this.direction()?.frames ?? []).map((f) => [
        f.firstElementId,
        picturing.has(f.id) ? 'generating' : imageState(f.image),
      ]),
    );
  });
  /** Saved frames without a picture, or whose picture has fallen behind, in scene order. */
  readonly framesNeedingImages = computed(() =>
    this.frameSpans()
      .map((s) => s.frame)
      .filter((f) => f.id && ['none', 'stale'].includes(imageState(f.image)))
      .map((f) => f.id),
  );
  /** The scene's cast, places, props and extras, as library targets. */
  readonly sceneEntries = computed<LibraryTarget[]>(() => [
    ...this.cast().map((c) => ({ kind: 'character' as const, id: c.id })),
    ...this.locationsInScene().map((l) => ({
      kind: 'location' as const,
      id: l.id,
    })),
    ...this.propsInScene().map((p) => ({ kind: 'prop' as const, id: p.id })),
    ...this.extrasInScene().map((e) => ({ kind: 'extra' as const, id: e.id })),
  ]);

  libraryImageState(id: string): ImageState {
    return this.picturing().has(id)
      ? 'generating'
      : imageState(this.libraryImages().get(id)?.image);
  }

  /** What the story text has outgrown in the director layer, in scene order. */
  readonly changes = computed(() => {
    const direction = this.direction();
    return direction ? directionChanges(direction, this.elements()) : [];
  });

  frameAt(index: number): FrameSpan | null {
    return spanAt(this.frameSpans(), index);
  }

  /** The model is writing this target, alone or as part of a wider one. */
  isDirecting(target: DirectTarget): boolean {
    return this.directing().some((running) => covers(running, target));
  }

  /** No request in flight writes any of what this one would. */
  canDirect(target: DirectTarget): boolean {
    return !this.directing().some((running) => covers(running, target) || covers(target, running));
  }

  readonly places = computed<ScenePlace[]>(() =>
    (this.story()?.containers ?? []).flatMap((container) =>
      container.scenes.map((summary, index) => ({ container, summary, index })),
    ),
  );
  private readonly placeIndex = computed(() => {
    const id = this.scene()?.id;
    return this.places().findIndex((p) => p.summary.id === id);
  });
  readonly place = computed(() => this.places()[this.placeIndex()] ?? null);
  readonly previous = computed(() => this.places()[this.placeIndex() - 1] ?? null);
  readonly next = computed(() => this.places()[this.placeIndex() + 1] ?? null);
  /** A story keeps at least one scene for the editor to open. */
  readonly canDeleteScene = computed(() => this.places().length > 1);

  ngOnDestroy(): void {
    this.player.stop();
    this.flushSave();
  }

  // ─── loading ───────────────────────────────────────────────────────────

  async open(storyId: string, sceneId: string | null): Promise<void> {
    const saved = this.flushSave();
    this.error.set('');
    try {
      if (this.story()?.id !== storyId) {
        this.loading.set(true);
        this.libraryImages.set(new Map());
        this.story.set(await this.api.getStory(storyId));
        void this.refreshLibraryImages();
      }
      const target = sceneId ?? this.places()[0]?.summary.id;
      if (!target) {
        this.error.set('This story has no scenes yet.');
        return;
      }
      if (!sceneId) {
        this.go(target, true);
        return;
      }
      if (this.scene()?.id !== target) {
        await saved;
        this.player.stop();
        const [scene, direction] = await Promise.all([
          this.api.getScene(target),
          this.directionApi.getSceneDirection(target),
        ]);
        this.scene.set(scene);
        this.direction.set(direction);
        this.selectedFrameKey.set(null);
        this.editingId.set(null);
      }
    } catch (err) {
      this.error.set(describeError(err, 'This story could not be loaded.'));
    } finally {
      this.loading.set(false);
    }
  }

  /** The page's route change then opens it. */
  go(sceneId: string, replaceUrl = false): void {
    const storyId = this.story()?.id;
    if (storyId) {
      void this.router.navigate(['/app/stories', storyId, 'scenes', sceneId], {
        replaceUrl,
        queryParams: this.mode() === 'director' ? { mode: 'director' } : {},
      });
    }
  }

  private async refreshOutline(): Promise<void> {
    const story = this.story();
    if (story) {
      this.story.set(await this.api.getStory(story.id));
    }
  }

  // ─── the scene ─────────────────────────────────────────────────────────

  private updateScene(update: (scene: Scene) => Scene): void {
    const scene = this.scene();
    if (!scene) {
      return;
    }
    this.scene.set(update(scene));
    this.sceneDirty = true;
    this.queueSave();
  }

  private updateDirection(update: (direction: SceneDirection) => SceneDirection): void {
    const scene = this.scene();
    if (!scene) {
      return;
    }
    this.direction.set(update(this.direction() ?? emptyDirection(scene.id)));
    this.directionDirty = true;
    this.directionVersion++;
    this.queueSave();
  }

  private queueSave(): void {
    if (this.saveTimer !== null) {
      clearTimeout(this.saveTimer);
    }
    this.saveTimer = setTimeout(() => this.flushSave(), SAVE_DELAY_MS);
  }

  /**
   * Saves go one at a time, so an older one never lands after a newer one or a library change.
   * The reply carries the rows' ids and edited marks, adopted unless the layer changed meanwhile.
   */
  private flushSave(): Promise<void> {
    if (this.saveTimer === null) {
      return this.saving;
    }
    clearTimeout(this.saveTimer);
    this.saveTimer = null;
    const scene = this.scene();
    const known = new Set(scene?.elements.map((e) => e.id));
    // A change on a deleted line goes with it, as the API's cascade would.
    const current = this.direction();
    const direction = current && {
      ...current,
      lookChanges: current.lookChanges.filter((c) => known.has(c.elementId)),
    };
    const { sceneDirty, directionDirty } = this;
    this.sceneDirty = false;
    this.directionDirty = false;
    if (!scene || (!sceneDirty && !directionDirty)) {
      return this.saving;
    }
    const version = this.directionVersion;
    const save = async () => {
      if (directionDirty && direction) {
        const saved = sceneDirty
          ? await this.directionApi.saveDirectedScene(scene, direction)
          : await this.directionApi.saveSceneDirection(direction);
        if (this.directionVersion === version && this.scene()?.id === saved.sceneId) {
          this.direction.set(saved);
        }
      } else {
        await this.api.saveScene(scene);
      }
    };
    this.saving = this.saving.then(() =>
      save().catch((err) => this.error.set(describeError(err, 'Your changes could not be saved.'))),
    );
    return this.saving;
  }

  setTitle(title: string): void {
    this.updateScene((scene) => ({ ...scene, title }));
  }

  /** Titles are required: an empty one reverts to the default. */
  commitTitle(): void {
    const scene = this.scene();
    if (!scene) {
      return;
    }
    const title = scene.title.trim() || UNTITLED;
    if (title !== scene.title) {
      this.setTitle(title);
    }
    this.flushSave();
    this.story.update(
      (story) =>
        story && {
          ...story,
          containers: story.containers.map((c) => ({
            ...c,
            scenes: c.scenes.map((s) => (s.id === scene.id ? { ...s, title } : s)),
          })),
        },
    );
  }

  updateElement(id: string, patch: Partial<SceneElement>): void {
    this.updateScene((scene) => ({
      ...scene,
      elements: scene.elements.map((e) => (e.id === id ? { ...e, ...patch } : e)),
    }));
  }

  /** In director mode the line's cue follows the words, so the edit resolves itself. */
  setText(id: string, value: MentionedText): void {
    this.updateElement(id, { text: value.text, mentions: value.mentions });
    const cue = this.cueByElement().get(id);
    if (this.mode() === 'director' && cue && stripTags(cue.performance) !== value.text.trim()) {
      this.updateCue(id, {
        performance: reanchor(cue.performance, value.text),
      });
    }
  }

  /** Keeps only the fields the new type uses. */
  setBeatType(id: string, type: SceneElementType): void {
    const index = this.elements().findIndex((e) => e.id === id);
    const element = this.elements()[index];
    if (!element || element.type === type) {
      return;
    }
    const dialogue = type === 'Dialogue';
    this.updateElement(id, {
      type,
      speakerId: dialogue ? (element.speakerId ?? this.guessSpeaker(index)) : null,
      extension: dialogue ? element.extension : null,
      parenthetical: dialogue ? element.parenthetical : null,
    });
  }

  /** Text survives only between beat types; the editor converts to a heading or transition when empty. */
  changeType(id: string, type: SceneElementType): void {
    const element = this.elements().find((e) => e.id === id);
    if (!element || element.type === type) {
      return;
    }
    if (isBeat(type) && isBeat(element.type)) {
      this.setBeatType(id, type);
      return;
    }
    const index = this.elements().indexOf(element);
    this.updateElement(id, {
      type,
      text: isBeat(type) ? (element.text ?? '') : null,
      mentions: isBeat(type) ? element.mentions : [],
      locationId: null,
      timeOfDay: null,
      speakerId: type === 'Dialogue' ? this.guessSpeaker(index) : null,
      extension: null,
      parenthetical: null,
      transition: null,
    });
  }

  /** Whoever spoke before the last speaker, as a conversation alternates. */
  guessSpeaker(at: number): string | null {
    const speakers = this.elements()
      .slice(0, at)
      .filter((e) => e.type === 'Dialogue' && e.speakerId)
      .map((e) => e.speakerId!)
      .reverse();
    const last = speakers[0];
    const before = speakers.find((s) => s !== last);
    return (
      before ?? this.characters().find((c) => c.id !== last)?.id ?? this.characters()[0]?.id ?? null
    );
  }

  insertElement(
    at: number,
    type: SceneElementType,
    patch: Partial<SceneElement> = {},
  ): SceneElement | null {
    const scene = this.scene();
    if (!scene) {
      return null;
    }
    const element: SceneElement = {
      id: crypto.randomUUID(),
      sceneId: scene.id,
      position: at,
      type,
      text: type === 'Heading' || type === 'Transition' ? null : '',
      locationId: null,
      timeOfDay: null,
      speakerId: type === 'Dialogue' ? this.guessSpeaker(at) : null,
      extension: null,
      parenthetical: null,
      transition: type === 'Transition' ? DEFAULT_VOCABULARY.transitions[0] : null,
      mentions: [],
      ...patch,
    };
    this.updateScene((s) => ({
      ...s,
      elements: [...s.elements.slice(0, at), element, ...s.elements.slice(at)],
    }));
    this.editingId.set(element.id);
    return element;
  }

  removeElement(id: string): void {
    this.updateScene((scene) => ({
      ...scene,
      elements: scene.elements.filter((e) => e.id !== id),
    }));
    if (this.editingId() === id) {
      this.editingId.set(null);
    }
  }

  moveElement(id: string, delta: -1 | 1): void {
    const index = this.elements().findIndex((e) => e.id === id);
    const moved = swapElement(this.elements(), index, delta);
    if (moved) {
      this.updateScene((scene) => ({ ...scene, elements: moved }));
    }
  }

  dropBlock(from: number, dropAt: number): void {
    const moved = moveBlock(this.elements(), from, dropAt);
    if (moved) {
      this.updateScene((scene) => ({ ...scene, elements: moved }));
    }
  }

  insertReference(target: Referable): void {
    const element = this.elements().find((e) => e.id === this.editingId());
    if (!element || element.text === null) {
      return;
    }
    const text = element.text;
    const at = Math.min(this.caret() ?? text.length, text.length);
    const to = Math.min(Math.max(this.selectionEnd() ?? at, at), text.length);
    const current = { text, mentions: element.mentions };
    const { value, caret } =
      to > at ? tagReference(current, at, to, target) : insertReference(current, at, at, target);
    this.setText(element.id, value);
    this.caret.set(caret);
    this.selectionEnd.set(caret);
    this.caretRequest.set({ elementId: element.id, caret });
  }

  // ─── scenes and containers ─────────────────────────────────────────────

  async addScene(after: boolean): Promise<void> {
    const place = this.place();
    if (!place) {
      return;
    }
    const lastHeading = after
      ? [...this.elements()].reverse().find((e) => e.type === 'Heading')
      : undefined;
    const scene = this.blankScene(place.container.id, place.index + (after ? 1 : 0), {
      locationId: lastHeading?.locationId ?? null,
      timeOfDay: after ? 'LATER' : null,
    });
    await this.run(async () => {
      this.flushSave();
      await this.api.createScene(scene);
      await this.refreshOutline();
      this.openNew(scene);
    }, 'The scene could not be added.');
  }

  /** Opens the next scene, or the previous one when this was the last. */
  async deleteScene(): Promise<void> {
    const scene = this.scene();
    const then = this.next() ?? this.previous();
    if (!scene || !then) {
      return;
    }
    if (this.saveTimer !== null) {
      clearTimeout(this.saveTimer);
      this.saveTimer = null;
    }
    this.sceneDirty = false;
    this.directionDirty = false;
    await this.run(async () => {
      await this.saving;
      await this.api.deleteScene(scene.id);
      this.scene.set(null);
      this.editingId.set(null);
      await this.refreshOutline();
      this.go(then.summary.id, true);
    }, 'The scene could not be deleted.');
  }

  async createContainer(label: string): Promise<void> {
    const story = this.story();
    if (!story) {
      return;
    }
    await this.run(async () => {
      this.flushSave();
      const container = await this.api.createContainer(story.id, label);
      const scene = this.blankScene(container.id, 0, {
        locationId: null,
        timeOfDay: null,
      });
      await this.api.createScene(scene);
      await this.refreshOutline();
      this.openNew(scene);
    }, 'The container could not be added.');
  }

  async renameContainer(containerId: string, label: string): Promise<void> {
    await this.run(async () => {
      await this.api.renameContainer(containerId, label);
      this.story.update(
        (story) =>
          story && {
            ...story,
            containers: story.containers.map((c) =>
              c.id === containerId ? { ...c, label: label.trim() } : c,
            ),
          },
      );
    }, 'The container could not be renamed.');
  }

  private blankScene(containerId: string, position: number, heading: Partial<SceneElement>): Scene {
    const id = crypto.randomUUID();
    return {
      id,
      containerId,
      position,
      title: UNTITLED,
      elements: [
        {
          id: crypto.randomUUID(),
          sceneId: id,
          position: 0,
          type: 'Heading',
          text: null,
          locationId: null,
          timeOfDay: null,
          speakerId: null,
          extension: null,
          parenthetical: null,
          transition: null,
          mentions: [],
          ...heading,
        },
      ],
    };
  }

  private openNew(scene: Scene): void {
    this.scene.set(scene);
    this.editingId.set(scene.elements[0].id);
    this.titleRequest.set(true);
    this.go(scene.id);
  }

  // ─── the library ───────────────────────────────────────────────────────

  async addCharacter(name: string, kind = this.kinds()[0].label): Promise<Character | string> {
    const story = this.story();
    const problem = nameProblem(this.characters(), name, 'character');
    if (!story || problem) {
      return problem ?? '';
    }
    return this.create(
      () =>
        this.api.createCharacter(story.id, {
          name: name.trim(),
          kind,
          hue: nextHue(this.characters().length),
          description: null,
          appearance: null,
          voice: null,
          wardrobes: [],
        }),
      (s, c) => ({ ...s, characters: [...s.characters, c] }),
    );
  }

  async addLocation(name: string, setting: LocationSetting): Promise<Location | string> {
    const story = this.story();
    const problem = nameProblem(this.locations(), name, 'location');
    if (!story || problem) {
      return problem ?? '';
    }
    return this.create(
      () =>
        this.api.createLocation(story.id, {
          name: name.trim(),
          setting,
          hue: nextHue(this.locations().length, 3),
          description: null,
          appearance: null,
          acoustics: null,
        }),
      (s, l) => ({ ...s, locations: [...s.locations, l] }),
    );
  }

  async addProp(name: string): Promise<Prop | string> {
    const story = this.story();
    const problem = nameProblem(this.props(), name, 'prop');
    if (!story || problem) {
      return problem ?? '';
    }
    return this.create(
      () => this.api.createProp(story.id, { name: name.trim(), description: null }),
      (s, p) => ({ ...s, props: [...s.props, p] }),
    );
  }

  async addExtra(name: string): Promise<Extra | string> {
    const story = this.story();
    const problem = nameProblem(this.extras(), name, 'extra');
    if (!story || problem) {
      return problem ?? '';
    }
    return this.create(
      () =>
        this.api.createExtra(story.id, { name: name.trim(), description: null, wardrobe: null }),
      (s, e) => ({ ...s, extras: [...s.extras, e] }),
    );
  }

  /** Renames the extra or rewrites its sheet; the reply is what the API kept. */
  async saveExtra(extra: Extra): Promise<string | null> {
    const problem = nameProblem(this.extras(), extra.name, 'extra', extra.id);
    if (problem) {
      return problem;
    }
    return this.saveSheet({ kind: 'extra', id: extra.id }, () =>
      this.api.updateExtra({ ...extra, name: extra.name.trim() }),
    );
  }

  /** Takes them out of every scene and frame too. */
  async removeExtra(id: string): Promise<void> {
    await this.flushSave();
    await this.run(async () => {
      await this.api.deleteExtra(id);
      this.story.update((s) => s && { ...s, extras: s.extras.filter((e) => e.id !== id) });
      this.direction.update(
        (d) =>
          d && {
            ...d,
            extras: d.extras.filter((e) => e.extraId !== id),
            frames: d.frames.map((f) => ({
              ...f,
              extras: f.extras.filter((e) => e.extraId !== id),
            })),
          },
      );
      this.libraryImages.update((m) => {
        const next = new Map(m);
        next.delete(id);
        return next;
      });
    }, 'Those extras could not be removed.');
  }

  private async create<T>(
    call: () => Promise<T>,
    add: (story: Story, entry: T) => Story,
  ): Promise<T | string> {
    try {
      const entry = await call();
      this.story.update((story) => story && add(story, entry));
      return entry;
    } catch (err) {
      return describeError(err, 'That could not be added.');
    }
  }

  async rename(target: ReferenceTarget, name: string): Promise<string | null> {
    const trimmed = name.trim();
    const list = this.libraryList(target);
    const entry = list.find((e) => e.id === target.id);
    const problem = nameProblem(list, trimmed, target.kind, target.id);
    if (!entry || problem) {
      return problem;
    }
    if (entry.name === trimmed) {
      return null;
    }
    await this.flushSave();
    const error = await this.updateEntry(target, { name: trimmed });
    if (!error) {
      this.scene.update(
        (scene) =>
          scene && {
            ...scene,
            elements: scene.elements.map((e) => renameInElement(e, target, trimmed)),
          },
      );
    }
    return error;
  }

  async setCharacterKind(id: string, kind: string): Promise<void> {
    await this.updateEntry({ kind: 'character', id }, { kind });
  }

  async setLocationSetting(id: string, setting: LocationSetting): Promise<void> {
    await this.updateEntry({ kind: 'location', id }, { setting });
  }

  async remove(target: ReferenceTarget): Promise<void> {
    await this.flushSave();
    await this.run(async () => {
      if (target.kind === 'character') {
        await this.api.deleteCharacter(target.id);
        this.story.update(
          (s) =>
            s && {
              ...s,
              characters: s.characters.filter((c) => c.id !== target.id),
            },
        );
      } else if (target.kind === 'location') {
        await this.api.deleteLocation(target.id);
        this.story.update(
          (s) =>
            s && {
              ...s,
              locations: s.locations.filter((l) => l.id !== target.id),
            },
        );
      } else {
        await this.api.deleteProp(target.id);
        this.story.update((s) => s && { ...s, props: s.props.filter((p) => p.id !== target.id) });
      }
      this.scene.update(
        (scene) =>
          scene && {
            ...scene,
            elements: scene.elements.map((e) => forgetInElement(e, target)),
          },
      );
    }, 'That could not be removed.');
  }

  private libraryList(target: ReferenceTarget): { id: string; name: string }[] {
    switch (target.kind) {
      case 'character':
        return this.characters();
      case 'location':
        return this.locations();
      case 'prop':
        return this.props();
    }
  }

  private async updateEntry(
    target: ReferenceTarget,
    patch: Record<string, unknown>,
  ): Promise<string | null> {
    const story = this.story();
    if (!story) {
      return null;
    }
    try {
      if (target.kind === 'character') {
        const next = { ...this.characterById().get(target.id)!, ...patch };
        await this.api.updateCharacter(next);
        this.story.set({
          ...story,
          characters: story.characters.map((c) => (c.id === target.id ? next : c)),
        });
      } else if (target.kind === 'location') {
        const next = { ...this.locationById().get(target.id)!, ...patch };
        await this.api.updateLocation(next);
        this.story.set({
          ...story,
          locations: story.locations.map((l) => (l.id === target.id ? next : l)),
        });
      } else {
        const next = { ...this.propById().get(target.id)!, ...patch };
        await this.api.updateProp(next);
        this.story.set({
          ...story,
          props: story.props.map((p) => (p.id === target.id ? next : p)),
        });
      }
      return null;
    } catch (err) {
      return describeError(err, 'That change could not be saved.');
    }
  }

  // ─── vocabulary ────────────────────────────────────────────────────────

  /** Uppercased, as headings print it. */
  async addTimeOfDay(label: string): Promise<string | null> {
    const problem = labelProblem(this.timesOfDay(), label);
    return (
      problem ??
      this.saveVocabulary((v) => ({
        ...v,
        timesOfDay: [...v.timesOfDay, label.trim().toUpperCase()],
      }))
    );
  }

  async addTransition(label: string): Promise<string | null> {
    const problem = labelProblem(this.transitions(), label);
    return (
      problem ??
      this.saveVocabulary((v) => ({
        ...v,
        transitions: [...v.transitions, label.trim().toUpperCase()],
      }))
    );
  }

  /** Uppercased and without parentheses, as the cue prints it inside them. */
  async addExtension(label: string): Promise<string | null> {
    const problem = labelProblem(this.extensions(), label);
    return (
      problem ??
      this.saveVocabulary((v) => ({
        ...v,
        extensions: [...v.extensions, label.trim().toUpperCase()],
      }))
    );
  }

  /** Lowercased, as a cue prints it. */
  async addDeliveryTag(label: string): Promise<string | null> {
    const problem = labelProblem(this.deliveryTags(), label);
    return (
      problem ??
      this.saveVocabulary((v) => ({
        ...v,
        deliveryTags: [...v.deliveryTags, label.trim().toLowerCase()],
      }))
    );
  }

  async addCharacterKind(kind: CharacterKind): Promise<string | null> {
    const problem = labelProblem(
      this.kinds().map((k) => k.label),
      kind.label,
    );
    return (
      problem ??
      this.saveVocabulary((v) => ({
        ...v,
        characterKinds: [
          ...v.characterKinds,
          { label: kind.label.trim(), avatarShape: kind.avatarShape },
        ],
      }))
    );
  }

  /** A label no longer in the vocabulary draws as a circle. */
  kindOf(character: Character): CharacterKind {
    return (
      this.kinds().find((k) => sameName(k.label, character.kind)) ?? {
        label: character.kind,
        avatarShape: 'Circle',
      }
    );
  }

  private async saveVocabulary(
    update: (v: StoryVocabulary) => StoryVocabulary,
  ): Promise<string | null> {
    const story = this.story();
    if (!story) {
      return null;
    }
    const vocabulary = update(story.vocabulary);
    try {
      await this.api.saveVocabulary(story.id, vocabulary);
      this.story.set({ ...story, vocabulary });
      return null;
    } catch (err) {
      return describeError(err, 'The label could not be added.');
    }
  }

  // ─── director mode: cues ───────────────────────────────────────────────

  /** A cue the author writes by hand starts as the line's own words. */
  ensureCue(elementId: string): Cue | null {
    const existing = this.cueByElement().get(elementId);
    if (existing) {
      return existing;
    }
    const element = this.elements().find((e) => e.id === elementId);
    if (!element || !isSpoken(element.type)) {
      return null;
    }
    const cue: Cue = {
      id: '',
      elementId,
      performance: (element.text ?? '').replace(/@/g, '').trim(),
      gapBeforeMs: 0,
      interrupted: false,
      narratorId: null,
      edited: true,
      audio: null,
    };
    this.updateDirection((d) => ({ ...d, cues: [...d.cues, cue] }));
    return cue;
  }

  updateCue(elementId: string, patch: Partial<Cue>): void {
    if (!this.ensureCue(elementId)) {
      return;
    }
    this.updateDirection((d) => ({
      ...d,
      cues: d.cues.map((c) => (c.elementId === elementId ? { ...c, ...patch, edited: true } : c)),
    }));
  }

  /** False when the cue has its three tags or that tag already. */
  addCueTag(elementId: string, beforeWord: number, tag: string): boolean {
    const cue = this.ensureCue(elementId);
    if (!cue) {
      return false;
    }
    const performance = insertTag(cue.performance, beforeWord, tag);
    if (performance === null) {
      return false;
    }
    this.updateCue(elementId, { performance });
    return true;
  }

  removeCueTag(elementId: string, tag: string): void {
    const cue = this.cueByElement().get(elementId);
    if (cue) {
      this.updateCue(elementId, {
        performance: removeTag(cue.performance, tag),
      });
    }
  }

  /** Carries a stale cue's tags onto the line as it reads now. */
  reconcileCue(elementId: string): void {
    const cue = this.cueByElement().get(elementId);
    const element = this.elements().find((e) => e.id === elementId);
    if (cue && element) {
      this.updateCue(elementId, {
        performance: reanchor(cue.performance, element.text ?? ''),
      });
    }
  }

  /** Null hands the line back to the story's first narrator. */
  setNarrator(elementId: string, narratorId: string | null): void {
    this.updateCue(elementId, { narratorId });
  }

  // ─── director mode: frames ─────────────────────────────────────────────

  selectFrame(key: string | null): void {
    this.selectedFrameKey.set(key);
    if (key !== null) {
      this.inspectorScope.set('frame');
    }
  }

  stepFrame(delta: -1 | 1): void {
    const spans = this.frameSpans();
    const at = spans.indexOf(this.selectedFrame()!);
    const next = spans[Math.min(Math.max(at + delta, 0), spans.length - 1)];
    if (next) {
      this.selectFrame(next.frame.firstElementId);
    }
  }

  splitFrameAt(index: number): void {
    const frames = splitAt(this.direction()?.frames ?? [], this.elements(), index);
    if (frames) {
      this.updateDirection((d) => ({ ...d, frames }));
      this.selectFrame(this.elements()[index].id);
    }
  }

  mergeFrameUp(index: number): void {
    const frames = mergeUp(this.direction()?.frames ?? [], this.elements(), index);
    if (frames) {
      this.updateDirection((d) => ({ ...d, frames }));
      this.selectFrame(
        spanAt(frameSpans(frames, this.elements()), index)?.frame.firstElementId ?? null,
      );
    }
  }

  coverRow(index: number): void {
    const frames = cover(this.direction()?.frames ?? [], this.elements(), index);
    if (frames) {
      this.updateDirection((d) => ({ ...d, frames }));
      this.selectFrame(
        spanAt(frameSpans(frames, this.elements()), index)?.frame.firstElementId ?? null,
      );
    }
  }

  updateFrame(key: string, patch: Partial<Frame>): void {
    this.updateDirection((d) => ({
      ...d,
      frames: renumber(
        d.frames.map((p) => (p.firstElementId === key ? { ...p, ...patch, edited: true } : p)),
        this.elements(),
      ),
    }));
  }

  /** In the frame scope a value equal to the scene's follows it rather than overriding it. */
  setFraming(key: Framing, value: string, frameKey: string | null): void {
    const direction = this.direction() ?? emptyDirection(this.scene()?.id ?? '');
    if (frameKey === null) {
      this.updateDirection((d) => ({ ...d, [key]: value }));
    } else {
      this.updateFrame(frameKey, {
        [key]: value === direction[key] ? null : value,
      });
    }
  }

  setOnScreen(characterId: string, on: boolean, frameKey: string | null): void {
    if (frameKey === null) {
      this.setAppearance(characterId, { onScreen: on });
      return;
    }
    const frame = this.frameByKey(frameKey);
    const direction = this.direction();
    if (!frame || !direction) {
      return;
    }
    const scene = onScreenOf(direction, null, characterId).value;
    this.updateFrame(frameKey, {
      placements: withPlacement(frame, characterId, {
        onScreen: on === scene ? null : on,
      }),
    });
  }

  setInView(propId: string, on: boolean, frameKey: string | null): void {
    if (frameKey === null) {
      this.setPropState(propId, { inView: on });
      return;
    }
    const frame = this.frameByKey(frameKey);
    const direction = this.direction();
    if (!frame || !direction) {
      return;
    }
    const scene = inViewOf(direction, null, propId).value;
    const rest = frame.props.filter((p) => p.propId !== propId);
    this.updateFrame(frameKey, {
      props: on === scene ? rest : [...rest, { propId, inView: on }],
    });
  }

  /** The frame follows the scene again for who is on screen. */
  resetCast(frameKey: string): void {
    const frame = this.frameByKey(frameKey);
    if (frame) {
      this.updateFrame(frameKey, {
        placements: frame.placements.flatMap((p) =>
          withPlacement({ ...frame, placements: [p] }, p.characterId, {
            onScreen: null,
          }),
        ),
      });
    }
  }

  resetProps(frameKey: string): void {
    this.updateFrame(frameKey, { props: [] });
  }

  setExtraOnScreen(extraId: string, on: boolean, frameKey: string | null): void {
    if (frameKey === null) {
      this.updateDirection((d) => ({
        ...d,
        extras: d.extras.map((e) =>
          e.extraId === extraId ? { ...e, onScreen: on, edited: true } : e,
        ),
      }));
      return;
    }
    const frame = this.frameByKey(frameKey);
    const direction = this.direction();
    if (!frame || !direction) {
      return;
    }
    const scene = extraOnScreenOf(direction, null, extraId).value;
    const rest = frame.extras.filter((e) => e.extraId !== extraId);
    this.updateFrame(frameKey, {
      extras: on === scene ? rest : [...rest, { extraId, onScreen: on }],
    });
  }

  resetExtras(frameKey: string): void {
    this.updateFrame(frameKey, { extras: [] });
  }

  addExtraToScene(extraId: string): void {
    this.updateDirection((d) =>
      d.extras.some((e) => e.extraId === extraId)
        ? d
        : { ...d, extras: [...d.extras, blankSceneExtra(extraId)] },
    );
  }

  /** Frames stop saying anything about them too. */
  removeExtraFromScene(extraId: string): void {
    this.updateDirection((d) => ({
      ...d,
      extras: d.extras.filter((e) => e.extraId !== extraId),
      frames: d.frames.map((f) =>
        f.extras.some((e) => e.extraId === extraId)
          ? { ...f, extras: f.extras.filter((e) => e.extraId !== extraId), edited: true }
          : f,
      ),
    }));
  }

  /** In the scene scope a wardrobe goes on the scene's look; in a frame, on its placement. */
  setLook(
    characterId: string,
    field: 'wardrobeId' | 'wardrobeChange' | 'appearanceChange',
    value: string | null,
    frameKey: string | null,
  ): void {
    if (frameKey === null) {
      this.setAppearance(characterId, {
        [field]: value ?? (field === 'wardrobeId' ? null : ''),
      });
      return;
    }
    const frame = this.frameByKey(frameKey);
    if (frame) {
      this.updateFrame(frameKey, {
        placements: withPlacement(frame, characterId, {
          [field]: value ?? (field === 'wardrobeId' ? null : ''),
        }),
      });
    }
  }

  /** A frame's look moves to the scene, so every frame has it; no frame keeps its own. */
  promoteLook(
    characterId: string,
    field: 'wardrobeId' | 'wardrobeChange' | 'appearanceChange',
    frameKey: string,
  ): void {
    const own = this.frameByKey(frameKey)?.placements.find((p) => p.characterId === characterId);
    const scene = this.appearanceByCharacter().get(characterId);
    if (!own) {
      return;
    }
    const value =
      field === 'wardrobeId' ? own.wardrobeId : joinChanges(scene?.[field] ?? '', own[field]);
    this.setAppearance(characterId, { [field]: value });
    this.clearFrameLooks(characterId, field);
  }

  clearFrameLooks(
    characterId: string,
    field: 'wardrobeId' | 'wardrobeChange' | 'appearanceChange',
  ): void {
    const blank = field === 'wardrobeId' ? null : '';
    this.updateDirection((d) => ({
      ...d,
      frames: d.frames.map((p) =>
        p.placements.some((pl) => pl.characterId === characterId && pl[field])
          ? {
              ...p,
              placements: withPlacement(p, characterId, { [field]: blank }),
              edited: true,
            }
          : p,
      ),
    }));
  }

  /** Frames that set their own value for a character's look field, by number. */
  framesWithLook(
    characterId: string,
    field: 'wardrobeId' | 'wardrobeChange' | 'appearanceChange',
  ): number[] {
    return this.frameSpans().flatMap((s, i) =>
      s.frame.placements.some((p) => p.characterId === characterId && p[field]) ? [i + 1] : [],
    );
  }

  setLocationChange(locationId: string, change: string, frameKey: string | null): void {
    if (frameKey !== null) {
      this.updateFrame(frameKey, { locationChange: change });
      return;
    }
    this.updateDirection((d) => {
      const rest = d.locationStates.filter((l) => l.locationId !== locationId);
      const current = d.locationStates.find((l) => l.locationId === locationId);
      return {
        ...d,
        locationStates: change.trim()
          ? [...rest, { id: current?.id ?? '', locationId, change, edited: true }]
          : rest,
      };
    });
  }

  promoteLocationChange(locationId: string, frameKey: string): void {
    const own = this.frameByKey(frameKey)?.locationChange ?? '';
    const scene = this.locationStateByLocation().get(locationId)?.change ?? '';
    this.setLocationChange(locationId, joinChanges(scene, own), null);
    this.clearFrameLocationChanges(locationId);
  }

  clearFrameLocationChanges(locationId: string): void {
    const elements = this.elements();
    this.updateDirection((d) => ({
      ...d,
      frames: d.frames.map((p) => {
        const first = elements.findIndex((e) => e.id === p.firstElementId);
        const at = headingAt(elements, first)?.locationId ?? null;
        return p.locationChange && at === locationId
          ? { ...p, locationChange: '', edited: true }
          : p;
      }),
    }));
  }

  deleteFrame(key: string): void {
    const frame = this.frameByKey(key);
    if (frame) {
      this.updateDirection((d) => ({
        ...d,
        frames: removeFrame(d.frames, this.elements(), frame),
      }));
      if (this.selectedFrameKey() === key) {
        this.selectedFrameKey.set(null);
      }
    }
  }

  private frameByKey(key: string): Frame | null {
    return this.direction()?.frames.find((p) => p.firstElementId === key) ?? null;
  }

  // ─── director mode: continuity ─────────────────────────────────────────

  /**
   * In the scene scope, what they carry as the scene opens. In a frame, what they carry from that
   * frame on: the change that sets the frame's look when it starts inside the frame, else a new
   * change on the frame's first row. A change that matches the look before it is dropped.
   */
  setHolding(characterId: string, holding: string[], frameKey: string | null): void {
    if (frameKey === null) {
      this.setAppearance(characterId, { holding });
      return;
    }
    const direction = this.direction();
    const order = this.elementOrder();
    const span = this.frameSpans().find((s) => s.frame.firstElementId === frameKey);
    if (!direction || !span) {
      return;
    }
    const inForce = lookAt(direction, characterId, order, span.last).change;
    const elementId =
      inForce && (order.get(inForce.elementId) ?? -1) >= span.first ? inForce.elementId : frameKey;
    const before = lookAt(direction, characterId, order, (order.get(elementId) ?? 0) - 1);
    const current =
      direction.lookChanges.find(
        (c) => c.characterId === characterId && c.elementId === elementId,
      ) ??
      ({
        id: '',
        characterId,
        elementId,
        wardrobeId: before.wardrobeId,
        wardrobeChange: before.wardrobeChange,
        appearanceChange: before.appearanceChange,
        holding: before.holding,
        reason: null,
        edited: true,
        image: null,
      } satisfies LookChange);
    const next = { ...current, holding, edited: true };
    const same =
      next.wardrobeId === before.wardrobeId &&
      next.wardrobeChange === before.wardrobeChange &&
      next.appearanceChange === before.appearanceChange &&
      next.holding.length === before.holding.length &&
      next.holding.every((id) => before.holding.includes(id));
    this.updateDirection((d) => ({
      ...d,
      lookChanges: [
        ...d.lookChanges.filter(
          (c) => !(c.characterId === characterId && c.elementId === elementId),
        ),
        ...(same ? [] : [next]),
      ],
    }));
  }

  removeLookChange(characterId: string, elementId: string): void {
    this.updateDirection((d) => ({
      ...d,
      lookChanges: d.lookChanges.filter(
        (c) => !(c.characterId === characterId && c.elementId === elementId),
      ),
    }));
  }

  /** Selects the frame that pictures a row. */
  showRow(elementId: string): void {
    const at = this.elementOrder().get(elementId);
    const span = this.frameSpans().find((s) => at !== undefined && s.first <= at && at <= s.last);
    if (span) {
      this.selectFrame(span.frame.firstElementId);
    }
  }

  setAppearance(characterId: string, patch: Partial<SceneAppearance>): void {
    this.updateDirection((d) => {
      const current =
        d.appearances.find((a) => a.characterId === characterId) ?? blankAppearance(characterId);
      return {
        ...d,
        appearances: [
          ...d.appearances.filter((a) => a.characterId !== characterId),
          { ...current, ...patch, edited: true },
        ],
      };
    });
  }

  setPropState(propId: string, patch: Partial<ScenePropState>): void {
    this.updateDirection((d) => {
      const current = d.propStates.find((p) => p.propId === propId) ?? blankPropState(propId);
      return {
        ...d,
        propStates: [
          ...d.propStates.filter((p) => p.propId !== propId),
          { ...current, ...patch, edited: true },
        ],
      };
    });
  }

  setStoryDay(storyDay: number | null): void {
    this.updateDirection((d) => ({ ...d, storyDay }));
  }

  // ─── director mode: the model ──────────────────────────────────────────

  /** Pending edits save first, so the model reads the scene as the author sees it. */
  async direct(target: DirectTarget, replaceEdited = false): Promise<void> {
    const scene = this.scene();
    if (!scene || !this.canDirect(target)) {
      return;
    }
    this.directing.update((running) => [...running, target]);
    try {
      await this.flushSave();
      const { direction, extras } = await this.directionApi.direct(scene.id, target, replaceEdited);
      this.story.update((s) => s && { ...s, extras });
      if (this.scene()?.id === direction.sceneId) {
        if (this.directionDirty) {
          // Edits made while the model worked are kept; the next save carries them over its result.
          this.direction.update(
            (mine) => mine && { ...direction, ...this.ownEdits(mine, direction) },
          );
        } else {
          this.direction.set(direction);
        }
      }
    } catch (err) {
      this.error.set(describeError(err, 'The scene could not be directed.'));
    } finally {
      this.directing.update((running) => running.filter((t) => t !== target));
    }
  }

  /** Rows the author touched while a model request was out: they win over the model's. */
  private ownEdits(mine: SceneDirection, theirs: SceneDirection): Partial<SceneDirection> {
    const merge = <T>(
      ours: T[],
      others: T[],
      key: (row: T) => string,
      edited: (row: T) => boolean,
    ) => [
      ...others.filter((o) => !ours.some((m) => edited(m) && key(m) === key(o))),
      ...ours.filter(edited),
    ];
    return {
      cues: merge(
        mine.cues,
        theirs.cues,
        (c) => c.elementId,
        (c) => c.edited,
      ),
      appearances: merge(
        mine.appearances,
        theirs.appearances,
        (a) => a.characterId,
        (a) => a.edited,
      ),
      lookChanges: merge(
        mine.lookChanges,
        theirs.lookChanges,
        (c) => `${c.characterId}:${c.elementId}`,
        (c) => c.edited,
      ),
      propStates: merge(
        mine.propStates,
        theirs.propStates,
        (p) => p.propId,
        (p) => p.edited,
      ),
      locationStates: merge(
        mine.locationStates,
        theirs.locationStates,
        (l) => l.locationId,
        (l) => l.edited,
      ),
      extras: merge(
        mine.extras,
        theirs.extras,
        (e) => e.extraId,
        (e) => e.edited,
      ),
      frames: mine.frames.some((p) => p.edited) ? mine.frames : theirs.frames,
    };
  }

  async describe(target: DescribeTarget): Promise<void> {
    await this.describeAll([target]);
  }

  /** Writes the sheets one at a time, stopping at the first that fails. */
  async describeAll(targets: readonly DescribeTarget[]): Promise<void> {
    if (this.describing() !== null || targets.length === 0) {
      return;
    }
    this.describing.set(targets[0].id);
    this.describeRun.set(targets.map((t) => t.id));
    try {
      await this.flushSave();
      for (const target of targets) {
        this.describing.set(target.id);
        this.replaceEntry(target, await this.directionApi.describe(target));
      }
      await Promise.all([this.refreshMedia(), this.refreshLibraryImages()]);
    } catch (err) {
      this.error.set(describeError(err, 'The sheet could not be written.'));
    } finally {
      this.describing.set(null);
      this.describeRun.set([]);
    }
  }

  // ─── director mode: audio ──────────────────────────────────────────────

  /** Speaks the lines one at a time, so each becomes playable as soon as it is spoken. */
  async generateAudio(elementIds: readonly string[], regenerate = false): Promise<void> {
    const scene = this.scene();
    const queued = elementIds.filter((id) => !this.generatingLines().has(id));
    if (!scene || queued.length === 0) {
      return;
    }
    this.generatingLines.update((ids) => new Set([...ids, ...queued]));
    try {
      await this.flushSave();
      for (const id of queued) {
        if (this.scene()?.id !== scene.id) {
          return;
        }
        this.adoptAudio(await this.directionApi.generateAudio(scene.id, id, regenerate), scene.id);
        this.generatingLines.update((ids) => new Set([...ids].filter((i) => i !== id)));
      }
    } catch (err) {
      this.error.set(describeError(err, 'The line could not be spoken.'));
    } finally {
      this.generatingLines.update((ids) => new Set([...ids].filter((i) => !queued.includes(i))));
    }
  }

  /** Only the audio comes from the reply, and cues the server made for unspoken lines. */
  private adoptAudio(direction: SceneDirection, sceneId: string): void {
    if (this.scene()?.id !== sceneId) {
      return;
    }
    const theirs = new Map(direction.cues.map((c) => [c.elementId, c]));
    this.direction.update((mine) => {
      if (!mine) {
        return mine;
      }
      const known = new Set(mine.cues.map((c) => c.elementId));
      return {
        ...mine,
        cues: [
          ...mine.cues.map((c) => ({
            ...c,
            audio: theirs.get(c.elementId)?.audio ?? null,
          })),
          ...direction.cues.filter((c) => !known.has(c.elementId)),
        ],
      };
    });
  }

  // ─── director mode: pictures ──────────────────────────────────────────

  /**
   * Pictures the frames one at a time in scene order, so each can be composed from the one before
   * it. A scene run first pictures the scene's library entries that have no current picture, since
   * those keep faces and places the same from frame to frame.
   */
  async generateFrameImages(
    frameIds: readonly string[],
    regenerate = false,
    withLibrary = false,
  ): Promise<void> {
    const scene = this.scene();
    const queued = frameIds.filter((id) => id && !this.picturing().has(id));
    if (!scene || queued.length === 0) {
      return;
    }
    this.picturing.update((ids) => new Set([...ids, ...queued]));
    try {
      await this.flushSave();
      if (withLibrary) {
        const entries = this.sceneEntries().filter((t) =>
          ['none', 'stale'].includes(this.libraryImageState(t.id)),
        );
        await this.generateLibraryImages(entries);
      }
      const order = this.frameSpans().map((s) => s.frame.id);
      for (const id of [...queued].sort((a, b) => order.indexOf(a) - order.indexOf(b))) {
        if (this.scene()?.id !== scene.id) {
          return;
        }
        this.adoptImages(
          await this.directionApi.generateFrameImage(scene.id, id, regenerate),
          scene.id,
        );
        this.picturing.update((ids) => new Set([...ids].filter((i) => i !== id)));
      }
    } catch (err) {
      this.error.set(describeError(err, 'The frame could not be pictured.'));
    } finally {
      this.picturing.update((ids) => new Set([...ids].filter((i) => !queued.includes(i))));
    }
  }

  /** Pictures a character in a look of the scene: the opening look when `elementId` is null. */
  async generateLookImage(
    characterId: string,
    elementId: string | null,
    regenerate = false,
  ): Promise<void> {
    const scene = this.scene();
    const key = `look:${characterId}:${elementId ?? ''}`;
    if (!scene || this.picturing().has(key)) {
      return;
    }
    this.picturing.update((ids) => new Set([...ids, key]));
    try {
      await this.flushSave();
      this.adoptImages(
        await this.directionApi.generateLookImage(scene.id, characterId, elementId, regenerate),
        scene.id,
      );
    } catch (err) {
      this.error.set(describeError(err, 'The look could not be pictured.'));
    } finally {
      this.picturing.update((ids) => new Set([...ids].filter((i) => i !== key)));
    }
  }

  /** Pictures library entries one at a time; frames composed from a redone picture go stale. */
  async generateLibraryImages(
    targets: readonly LibraryTarget[],
    regenerate = false,
  ): Promise<void> {
    const queued = targets.filter((t) => !this.picturing().has(t.id));
    if (queued.length === 0) {
      return;
    }
    this.picturing.update((ids) => new Set([...ids, ...queued.map((t) => t.id)]));
    try {
      for (const target of queued) {
        const image = await this.directionApi.generateLibraryImage(target, regenerate);
        if (image) {
          this.libraryImages.update((m) => new Map(m).set(target.id, image));
        }
        this.picturing.update((ids) => new Set([...ids].filter((i) => i !== target.id)));
      }
      await this.refreshMedia();
    } catch (err) {
      this.error.set(describeError(err, 'The picture could not be made.'));
    } finally {
      const done = new Set(queued.map((t) => t.id));
      this.picturing.update((ids) => new Set([...ids].filter((i) => !done.has(i))));
    }
  }

  /** Only the pictures come from the reply. */
  private adoptImages(direction: SceneDirection, sceneId: string): void {
    if (this.scene()?.id !== sceneId) {
      return;
    }
    const theirs = new Map(direction.frames.map((f) => [f.firstElementId, f.image]));
    const looks = new Map(direction.appearances.map((a) => [a.characterId, a.image]));
    const changes = new Map(
      direction.lookChanges.map((c) => [`${c.characterId}:${c.elementId}`, c.image]),
    );
    this.direction.update(
      (mine) =>
        mine && {
          ...mine,
          frames: mine.frames.map((f) => ({
            ...f,
            image: theirs.get(f.firstElementId) ?? null,
          })),
          appearances: mine.appearances.map((a) => ({
            ...a,
            image: looks.get(a.characterId) ?? null,
          })),
          lookChanges: mine.lookChanges.map((c) => ({
            ...c,
            image: changes.get(`${c.characterId}:${c.elementId}`) ?? null,
          })),
        },
    );
  }

  private async refreshLibraryImages(): Promise<void> {
    const story = this.story();
    if (!story) {
      return;
    }
    try {
      const images = await this.directionApi.listLibraryImages(story.id);
      if (this.story()?.id === story.id) {
        this.libraryImages.set(new Map(images.map((i) => [i.target.id, i])));
      }
    } catch {
      // Pictures are an extra: the editor works without them.
    }
  }

  /** Lines in order that have audio to play, from the given element ids. */
  playLines(elementIds: readonly string[], context: string): void {
    const cues = this.cueByElement();
    const playable = elementIds.flatMap((id) => (cues.get(id)?.audio ? [cues.get(id)!] : []));
    if (playable.length > 0) {
      void this.player.playRun(playable, context);
    }
  }

  /** Errors are reported, and come back as no voices. */
  async searchVoices(query: VoiceQuery): Promise<SpeechVoice[]> {
    try {
      return await this.directionApi.searchVoices(query);
    } catch (err) {
      this.error.set(describeError(err, 'The voices could not be listed.'));
      return [];
    }
  }

  async castVoice(characterId: string): Promise<void> {
    if (this.castingVoice() !== null) {
      return;
    }
    this.castingVoice.set(characterId);
    try {
      await this.flushSave();
      this.replaceEntry(
        { kind: 'character', id: characterId },
        await this.directionApi.castVoice(characterId),
      );
      await this.refreshMedia();
    } catch (err) {
      this.error.set(describeError(err, 'No voice could be cast.'));
    } finally {
      this.castingVoice.set(null);
    }
  }

  setCharacterVoice(character: Character, voice: SpeechVoice): Promise<string | null> {
    return this.saveCharacterSheet({
      ...character,
      voice: {
        ...(character.voice ?? EMPTY_VOICE),
        voiceId: voice.id,
        voiceName: voice.name,
        previewUrl: voice.previewUrl,
      },
    });
  }

  // ─── narrators ─────────────────────────────────────────────────────────

  /** The story's first narrator becomes its default. */
  addNarrator(voice: SpeechVoice): void {
    if (this.narrators().some((n) => n.voice.voiceId === voice.id)) {
      return;
    }
    void this.saveNarrators([
      ...this.narrators(),
      {
        id: '',
        name: voice.name.slice(0, NARRATOR_TEXT_MAX),
        voice: {
          ...EMPTY_VOICE,
          description: voice.description.slice(0, NARRATOR_TEXT_MAX),
          voiceId: voice.id,
          voiceName: voice.name,
          previewUrl: voice.previewUrl,
        },
      },
    ]);
  }

  /** The default goes first; lines that picked it now take it by default. */
  makeDefaultNarrator(id: string): void {
    const narrators = this.narrators();
    const chosen = narrators.find((n) => n.id === id);
    if (!chosen) {
      return;
    }
    if (this.direction()?.cues.some((c) => c.narratorId === id)) {
      this.updateDirection((d) => ({
        ...d,
        cues: d.cues.map((c) =>
          c.narratorId === id ? { ...c, narratorId: null, edited: true } : c,
        ),
      }));
    }
    void this.saveNarrators([chosen, ...narrators.filter((n) => n.id !== id)]);
  }

  /** Their lines fall back to the default. */
  removeNarrator(id: string): void {
    void this.saveNarrators(this.narrators().filter((n) => n.id !== id));
  }

  private async saveNarrators(narrators: Narrator[]): Promise<void> {
    const story = this.story();
    if (!story) {
      return;
    }
    this.story.set({ ...story, narrators });
    await this.run(async () => {
      await this.flushSave();
      const saved = await this.api.saveNarrators(story.id, narrators);
      this.story.update((s) => s && { ...s, narrators: saved });
      const ids = new Set(saved.map((n) => n.id));
      this.direction.update(
        (d) =>
          d && {
            ...d,
            cues: d.cues.map((c) =>
              c.narratorId && !ids.has(c.narratorId) ? { ...c, narratorId: null } : c,
            ),
          },
      );
      await this.refreshMedia();
    }, 'The narrators could not be saved.');
  }

  /** Whether audio and pictures are still current is the server's call; a voice or a sheet change moves it. */
  private async refreshMedia(): Promise<void> {
    const scene = this.scene();
    if (scene && this.direction()) {
      const direction = await this.directionApi.getSceneDirection(scene.id);
      this.adoptAudio(direction, scene.id);
      this.adoptImages(direction, scene.id);
    }
  }

  // ─── sheets ────────────────────────────────────────────────────────────

  async saveCharacterSheet(character: Character): Promise<string | null> {
    return this.saveSheet({ kind: 'character', id: character.id }, () =>
      this.api.updateCharacterSheet(character),
    );
  }

  async saveLocationSheet(location: Location): Promise<string | null> {
    return this.saveSheet({ kind: 'location', id: location.id }, () =>
      this.api.updateLocationSheet(location),
    );
  }

  async savePropSheet(prop: Prop): Promise<string | null> {
    return this.saveSheet({ kind: 'prop', id: prop.id }, () => this.api.updatePropSheet(prop));
  }

  private async saveSheet(
    target: LibraryTarget,
    call: () => Promise<Character | Location | Prop | Extra>,
  ): Promise<string | null> {
    try {
      this.replaceEntry(target, await call());
      await Promise.all([this.refreshMedia(), this.refreshLibraryImages()]);
      return null;
    } catch (err) {
      return describeError(err, 'The sheet could not be saved.');
    }
  }

  private replaceEntry(target: LibraryTarget, entry: Character | Location | Prop | Extra): void {
    this.story.update((story) => {
      if (!story) {
        return story;
      }
      switch (target.kind) {
        case 'character':
          return {
            ...story,
            characters: story.characters.map((c) =>
              c.id === target.id ? (entry as Character) : c,
            ),
          };
        case 'location':
          return {
            ...story,
            locations: story.locations.map((l) => (l.id === target.id ? (entry as Location) : l)),
          };
        case 'prop':
          return {
            ...story,
            props: story.props.map((p) => (p.id === target.id ? (entry as Prop) : p)),
          };
        case 'extra':
          return {
            ...story,
            extras: story.extras.map((e) => (e.id === target.id ? (entry as Extra) : e)),
          };
      }
    });
  }

  private async run(call: () => Promise<void>, fallback: string): Promise<void> {
    try {
      await call();
    } catch (err) {
      this.error.set(describeError(err, fallback));
    }
  }
}

function imageState(image: { stale: boolean } | null | undefined): ImageState {
  return !image ? 'none' : image.stale ? 'stale' : 'ready';
}
