import { computed, inject, Injectable, OnDestroy, signal } from '@angular/core';
import { Router } from '@angular/router';
import { describeError } from '../../documents/document-api';
import { forgetInElement, renameInElement } from '../../stories/element-references';
import { labelProblem, nameProblem, sameName } from '../../stories/library-names';
import { insertReference } from '../../stories/mentions';
import { Character } from '../../stories/model/character';
import { CharacterKind } from '../../stories/model/character-kind';
import { DEFAULT_VOCABULARY } from '../../stories/model/default-vocabulary';
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
import { moveBlock, swapElement } from '../../stories/scene-blocks';
import { StoryApi } from '../../stories/story-api';
import { CaretRequest } from './caret-request';
import { nextHue } from './format';
import { LibraryTab } from './library-tab';
import { ScenePlace } from './scene-place';
import { SceneRow } from './scene-row';
import { SceneStats } from './scene-stats';

const SAVE_DELAY_MS = 400;
const UNTITLED = 'Untitled scene';

/** Scene edits save shortly after typing stops; library and outline changes save at once. */
@Injectable()
export class SceneEditorStore implements OnDestroy {
  private readonly api = inject(StoryApi);
  private readonly router = inject(Router);

  readonly story = signal<Story | null>(null);
  readonly scene = signal<Scene | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');

  readonly editingId = signal<string | null>(null);
  readonly libraryTab = signal<LibraryTab>('characters');
  /** For the library's "@ Insert". */
  readonly caret = signal<number | null>(null);
  readonly caretRequest = signal<CaretRequest | null>(null);
  readonly titleRequest = signal(false);

  private saveTimer: ReturnType<typeof setTimeout> | null = null;
  private saving: Promise<void> = Promise.resolve();

  readonly characters = computed(() => this.story()?.characters ?? []);
  readonly locations = computed(() => this.story()?.locations ?? []);
  readonly props = computed(() => this.story()?.props ?? []);
  readonly characterById = computed(() => new Map(this.characters().map((c) => [c.id, c])));
  readonly locationById = computed(() => new Map(this.locations().map((l) => [l.id, l])));
  readonly propById = computed(() => new Map(this.props().map((p) => [p.id, p])));

  private readonly vocabulary = computed<StoryVocabulary>(
    () => this.story()?.vocabulary ?? { timesOfDay: [], transitions: [], characterKinds: [] },
  );
  readonly timesOfDay = computed(() => [
    ...DEFAULT_VOCABULARY.timesOfDay,
    ...this.vocabulary().timesOfDay,
  ]);
  readonly transitions = computed(() => [
    ...DEFAULT_VOCABULARY.transitions,
    ...this.vocabulary().transitions,
  ]);
  readonly kinds = computed<CharacterKind[]>(() => [
    ...DEFAULT_VOCABULARY.characterKinds,
    ...this.vocabulary().characterKinds,
  ]);

  /** In `REFERENCE_ORDER`, as the autocomplete lists them. */
  readonly referables = computed<Referable[]>(() => [
    ...this.characters().map((c) => ({ kind: 'character' as const, id: c.id, name: c.name })),
    ...this.locations().map((l) => ({ kind: 'location' as const, id: l.id, name: l.name })),
    ...this.props().map((p) => ({ kind: 'prop' as const, id: p.id, name: p.name })),
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
    const stats: SceneStats = { lines: new Map(), mentions: new Map(), headings: new Map() };
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

  ngOnDestroy(): void {
    this.flushSave();
  }

  // ─── loading ───────────────────────────────────────────────────────────

  async open(storyId: string, sceneId: string | null): Promise<void> {
    const saved = this.flushSave();
    this.error.set('');
    try {
      if (this.story()?.id !== storyId) {
        this.loading.set(true);
        this.story.set(await this.api.getStory(storyId));
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
        this.scene.set(await this.api.getScene(target));
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
      void this.router.navigate(['/app/stories', storyId, 'scenes', sceneId], { replaceUrl });
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
    this.queueSave();
  }

  private queueSave(): void {
    if (this.saveTimer !== null) {
      clearTimeout(this.saveTimer);
    }
    this.saveTimer = setTimeout(() => this.flushSave(), SAVE_DELAY_MS);
  }

  /** Saves go one at a time, so an older one never lands after a newer one or a library change. */
  private flushSave(): Promise<void> {
    if (this.saveTimer === null) {
      return this.saving;
    }
    clearTimeout(this.saveTimer);
    this.saveTimer = null;
    const scene = this.scene();
    if (scene) {
      this.saving = this.saving.then(() =>
        this.api
          .saveScene(scene)
          .catch((err) => this.error.set(describeError(err, 'Your changes could not be saved.'))),
      );
    }
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

  setText(id: string, value: MentionedText): void {
    this.updateElement(id, { text: value.text, mentions: value.mentions });
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
      parenthetical: dialogue ? element.parenthetical : null,
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
    const { value, caret } = insertReference({ text, mentions: element.mentions }, at, at, target);
    this.setText(element.id, value);
    this.caret.set(caret);
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

  async createContainer(label: string): Promise<void> {
    const story = this.story();
    if (!story) {
      return;
    }
    await this.run(async () => {
      this.flushSave();
      const container = await this.api.createContainer(story.id, label);
      const scene = this.blankScene(container.id, 0, { locationId: null, timeOfDay: null });
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
      () => this.api.createProp(story.id, { name: name.trim() }),
      (s, p) => ({ ...s, props: [...s.props, p] }),
    );
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
          (s) => s && { ...s, characters: s.characters.filter((c) => c.id !== target.id) },
        );
      } else if (target.kind === 'location') {
        await this.api.deleteLocation(target.id);
        this.story.update(
          (s) => s && { ...s, locations: s.locations.filter((l) => l.id !== target.id) },
        );
      } else {
        await this.api.deleteProp(target.id);
        this.story.update((s) => s && { ...s, props: s.props.filter((p) => p.id !== target.id) });
      }
      this.scene.update(
        (scene) =>
          scene && { ...scene, elements: scene.elements.map((e) => forgetInElement(e, target)) },
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

  private async run(call: () => Promise<void>, fallback: string): Promise<void> {
    try {
      await call();
    } catch (err) {
      this.error.set(describeError(err, fallback));
    }
  }
}
