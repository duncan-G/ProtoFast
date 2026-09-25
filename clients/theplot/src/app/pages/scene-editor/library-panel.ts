import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { nameProblem, NAME_MAX } from '../../stories/library-names';
import { Character } from '../../stories/model/character';
import { Location } from '../../stories/model/location';
import { Prop } from '../../stories/model/prop';
import { ReferenceTarget } from '../../stories/model/reference-target';
import { Avatar } from './avatar';
import { initials, parseLocationQuery, plural, settingPrefix } from './format';
import { KindPicker } from './kind-picker';
import { LibraryTab } from './library-tab';
import { SceneEditorStore } from './scene-editor-store';

const TABS: { tab: LibraryTab; label: string; noun: string; hint: string; placeholder: string }[] =
  [
    {
      tab: 'characters',
      label: 'Characters',
      noun: 'character',
      hint: 'Anyone or anything with a voice — people, robots, animals.',
      placeholder: 'New character…',
    },
    {
      tab: 'locations',
      label: 'Locations',
      noun: 'location',
      hint: 'Places this story can happen. Start a stretch of the scene there with “+ Heading”.',
      placeholder: 'New location, e.g. EXT. Harbor',
    },
    {
      tab: 'props',
      label: 'Props',
      noun: 'prop',
      hint: 'Objects that matter to the plot. Reference them with @ while writing.',
      placeholder: 'New prop…',
    },
  ];

@Component({
  selector: 'app-library-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, KindPicker],
  host: { class: 'se-library', 'aria-label': 'Story library' },
  templateUrl: './library-panel.html',
})
export class LibraryPanel {
  protected readonly store = inject(SceneEditorStore);

  protected readonly tabs = TABS;
  protected readonly info = computed(() => TABS.find((t) => t.tab === this.store.libraryTab())!);
  protected readonly counts = computed<Record<LibraryTab, number>>(() => ({
    characters: this.store.characters().length,
    locations: this.store.locations().length,
    props: this.store.props().length,
  }));

  protected readonly draft = signal('');
  protected readonly addError = signal('');
  protected readonly nameMax = NAME_MAX;
  protected readonly renameError = signal<{ id: string; message: string } | null>(null);
  protected readonly confirming = signal<string | null>(null);
  protected readonly kindOpen = signal<string | null>(null);

  protected readonly editingText = computed(() => {
    const element = this.store.elements().find((e) => e.id === this.store.editingId());
    return !!element && element.text !== null;
  });

  protected readonly draftProblem = computed(() => {
    const draft = this.draft();
    if (!draft.trim()) {
      return null;
    }
    const tab = this.store.libraryTab();
    if (tab === 'locations') {
      return nameProblem(this.store.locations(), parseLocationQuery(draft).name, 'location');
    }
    return tab === 'characters'
      ? nameProblem(this.store.characters(), draft, 'character')
      : nameProblem(this.store.props(), draft, 'prop');
  });

  protected selectTab(tab: LibraryTab): void {
    this.store.libraryTab.set(tab);
    this.draft.set('');
    this.addError.set('');
    this.confirming.set(null);
  }

  protected initialsOf(name: string): string {
    return initials(name);
  }

  protected settingLabel(location: Location): string {
    return settingPrefix(location.setting).slice(0, 3);
  }

  protected characterMeta(character: Character): string {
    const stats = this.store.stats();
    return `${plural(stats.lines.get(character.id) ?? 0, 'line')} · ${plural(stats.mentions.get(character.id) ?? 0, 'mention')}`;
  }

  protected locationMeta(location: Location): string {
    const stats = this.store.stats();
    const headings = stats.headings.get(location.id) ?? 0;
    const mentions = stats.mentions.get(location.id) ?? 0;
    const parts = [
      headings ? `in scene ×${headings}` : '',
      mentions ? plural(mentions, 'mention') : '',
    ];
    return parts.filter(Boolean).join(' · ') || 'not in scene';
  }

  protected propMeta(prop: Prop): string {
    return plural(this.store.stats().mentions.get(prop.id) ?? 0, 'reference');
  }

  // ─── adding ────────────────────────────────────────────────────────────

  protected async add(): Promise<void> {
    const draft = this.draft().trim();
    if (!draft || this.draftProblem()) {
      return;
    }
    const tab = this.store.libraryTab();
    let result: unknown;
    if (tab === 'characters') {
      result = await this.store.addCharacter(draft);
    } else if (tab === 'locations') {
      const { setting, name } = parseLocationQuery(draft);
      result = await this.store.addLocation(name, setting ?? 'Interior');
    } else {
      result = await this.store.addProp(draft);
    }
    if (typeof result === 'string') {
      this.addError.set(result);
      return;
    }
    this.draft.set('');
    this.addError.set('');
  }

  // ─── renaming ──────────────────────────────────────────────────────────

  protected checkRename(
    target: ReferenceTarget,
    list: { id: string; name: string }[],
    name: string,
  ): void {
    const message = nameProblem(list, name, target.kind, target.id);
    this.renameError.set(message ? { id: target.id, message } : null);
  }

  protected async commitRename(
    target: ReferenceTarget,
    field: HTMLInputElement,
    current: string,
  ): Promise<void> {
    if (field.value === current) {
      this.renameError.set(null);
      return;
    }
    const problem = await this.store.rename(target, field.value);
    if (problem) {
      field.value = current;
    }
    this.renameError.set(null);
  }

  protected renameKey(event: KeyboardEvent, field: HTMLInputElement, current: string): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      if (!this.renameError()) {
        field.blur();
      }
    } else if (event.key === 'Escape') {
      field.value = current;
      this.renameError.set(null);
      field.blur();
    }
  }

  // ─── shortcuts into the scene ──────────────────────────────────────────

  protected addLine(character: Character): void {
    this.store.insertElement(this.store.elements().length, 'Dialogue', { speakerId: character.id });
  }

  protected addHeading(location: Location): void {
    this.store.insertElement(this.store.elements().length, 'Heading', {
      locationId: location.id,
      timeOfDay: 'DAY',
    });
  }

  protected insertProp(prop: Prop): void {
    this.store.insertReference({ kind: 'prop', id: prop.id, name: prop.name });
  }

  protected async remove(target: ReferenceTarget): Promise<void> {
    this.confirming.set(null);
    await this.store.remove(target);
  }
}
