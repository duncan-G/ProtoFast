import type {
  Scene,
  SceneItem,
  SceneLink,
  SectionNode,
  Situation,
} from '../../lib/gen/segmentation_pb';

/**
 * The scene stream, turned into what a page renders. Kept out of the component so it can be tested
 * without rendering anything, and so the template binds to plain properties rather than calling a
 * function per coordinate per change detection.
 *
 * The scene enumerations arrive as the lowercase strings the proto documents (`speech`,
 * `flashback_of`, `in_scene`) rather than as numbers, so everything here switches on a string. An
 * unrecognised value falls through to something readable rather than to a blank: a value this
 * client has not been taught yet is a new model member, not corrupt data.
 */

/** The coordinate strip under a scene's heading. */
export interface SituationLine {
  readonly place: string;
  /** The scene did not say where it is; the value was carried from the one before it (C7). */
  readonly placeInherited: boolean;
  readonly time: string;
  readonly cast: readonly CastLine[];
  readonly mode: string;
  readonly subject: string;
}

export interface CastLine {
  readonly personaId: string;
  readonly name: string;
  readonly role: string;
}

/** One item as it is shown. */
export interface ItemLine {
  readonly itemId: string;
  readonly kind: string;
  readonly text: string;
  /** True when `text` is generated staging text rather than the document's own words (§3.6). */
  readonly generated: boolean;
  readonly speaker: string;
  /** Narration and asides are spoken to the audience rather than to anyone in the scene. */
  readonly toAudience: boolean;
  readonly paragraphId: string;
}

export interface LinkLine {
  readonly label: string;
  readonly confidence: number;
  readonly showsConfidence: boolean;
  /** Empty when the target is outside the scenes on this page, which disables the jump. */
  readonly targetSceneId: string;
}

export interface SceneCard {
  readonly sceneId: string;
  readonly label: string;
  readonly titleInferred: boolean;
  readonly situation: SituationLine;
  readonly items: readonly ItemLine[];
  readonly links: readonly LinkLine[];
  readonly flags: readonly { readonly kind: string; readonly message: string }[];
  readonly modeHint: string;
}

/** One section's scenes, in reading order, under the path a reader would name it by. */
export interface SceneCardGroup {
  readonly sectionId: string;
  readonly path: string;
  readonly scenes: readonly SceneCard[];
}

/**
 * The whole page's view model: scenes grouped by the section that holds them.
 *
 * Grouping by `scene.sectionId` rather than by walking the tree is deliberate. The section tree
 * still carries paragraph ids — a scene names its section, not the other way round — and sections
 * are emitted in the order their first scene appears, so the page reads in document order however
 * deep the tree is.
 */
export function sceneCards(
  scenes: readonly Scene[],
  root: SectionNode | undefined,
): SceneCardGroup[] {
  const paths = sectionPaths(root);
  const at = ordinals(scenes);
  const groups: SceneCardGroup[] = [];
  const bySection = new Map<string, SceneCard[]>();

  for (const scene of scenes) {
    const card = sceneCard(scene, at);
    const existing = bySection.get(scene.sectionId);

    if (existing) {
      existing.push(card);
      continue;
    }

    const cards = [card];
    bySection.set(scene.sectionId, cards);
    groups.push({
      sectionId: scene.sectionId,
      path: paths.get(scene.sectionId) ?? '',
      scenes: cards,
    });
  }

  return groups;
}

export function sceneCard(scene: Scene, at: ReadonlyMap<string, number>): SceneCard {
  const situation = situationOf(scene.situation);

  return {
    sceneId: scene.sceneId,
    label: sceneLabel(scene),
    // An untitled scene is numbered rather than marked inferred: the number is this page's, not a
    // claim the pipeline made about the document.
    titleInferred: scene.titleInferred && scene.title.length > 0,
    situation,
    items: scene.items.map(itemLine),
    links: scene.links.map((link) => linkLine(link, at)),
    flags: scene.flags.map((flag) => ({ kind: flag.kind, message: flag.message })),
    modeHint: modeHint(situation.mode),
  };
}

/**
 * A scene's heading. A cut made mid-chapter has no title of its own and should not be given an
 * invented one, so it is numbered — and the ordinal is document-wide, which makes it the same
 * label a reviewer would cite.
 */
export function sceneLabel(scene: Scene): string {
  return scene.title || `Scene ${scene.ordinal + 1}`;
}

/**
 * Void renders as "no place", not as a blank: every coordinate is always present, and an empty
 * `place_id` is an answer about the scene rather than a gap in the data (C6).
 */
export function situationOf(situation: Situation | undefined): SituationLine {
  if (!situation) {
    return { place: 'no place', placeInherited: false, time: '', cast: [], mode: '', subject: '' };
  }

  return {
    // Falling back to the id rather than to "no place" when a place is named but unresolvable: the
    // scene did claim a setting, and hiding that would misreport the cut.
    place: situation.placeName || situation.placeId || 'no place',
    placeInherited: situation.settingSource === 'inherited',
    time: timeOf(situation),
    cast: situation.cast.map((member) => ({
      personaId: member.personaId,
      name: member.name,
      role: member.role,
    })),
    mode: situation.mode,
    subject: situation.subject,
  };
}

/** "the next morning · after a gap", or just the relation when nothing anchored it. */
export function timeOf(situation: Situation): string {
  const relation =
    situation.timeRelation === 'unanchored' ? '' : relationLabel(situation.timeRelation);

  if (situation.timeAnchor && relation) {
    return `${situation.timeAnchor} · ${relation}`;
  }

  return situation.timeAnchor || relation;
}

function relationLabel(relation: string): string {
  switch (relation) {
    case 'gap':
      return 'after a gap';
    case 'simultaneous':
      return 'at the same time';
    case 'continuous':
    case 'earlier':
    case 'later':
      return relation;
    default:
      return relation;
  }
}

/**
 * The text to stage, and whether it is the document's own.
 *
 * Render text wins when there is any: it exists precisely because the span could not be shown
 * alone — "— he said" is not a line — and it is generated and grounded rather than quoted (§3.7).
 * Marking which one a reader is looking at is not decoration: one is the work, the other is
 * ThePlot's staging of it.
 */
export function itemLine(item: SceneItem): ItemLine {
  const generated = item.renderText.length > 0;

  return {
    itemId: item.itemId,
    kind: item.kind,
    text: generated ? item.renderText : item.text,
    generated,
    speaker: item.speech?.speakerName ?? '',
    toAudience: item.speech?.addressee === 'audience',
    paragraphId: item.paragraphId,
  };
}

/** Scene id → its document-wide ordinal, so a link can name its target as a reader would cite it. */
export function ordinals(scenes: readonly Scene[]): Map<string, number> {
  return new Map(scenes.map((scene) => [scene.sceneId, scene.ordinal]));
}

/**
 * A link as a sentence fragment, with the jump it offers.
 *
 * `continues` is derived in code at phase 10 and the rest are inferred at phase 11, which is why
 * only the inferred ones show a confidence figure — a deterministic link has nothing to be
 * uncertain about.
 */
export function linkLine(link: SceneLink, at: ReadonlyMap<string, number>): LinkLine {
  const target = at.get(link.toSceneId);
  const named = target === undefined ? 'a scene outside this view' : `scene ${target + 1}`;

  return {
    label: `${linkPhrase(link.kind)} ${named}`,
    confidence: link.confidence,
    showsConfidence: link.kind !== 'continues' && link.confidence > 0 && link.confidence < 1,
    targetSceneId: target === undefined ? '' : link.toSceneId,
  };
}

function linkPhrase(kind: string): string {
  switch (kind) {
    case 'continues':
      return 'continues into';
    case 'returns_to':
      return 'returns to';
    case 'flashback_of':
      return 'a flashback of';
    case 'concurrent_with':
      return 'happens alongside';
    case 'frames':
      return 'frames';
    case 'framed_by':
      return 'framed by';
    default:
      return kind;
  }
}

/** The one enumeration worth explaining in place: the five mode names are terms of art. */
export function modeHint(mode: string): string {
  switch (mode) {
    case 'enacted':
      return 'Shown happening — in a place, to people';
    case 'narrated':
      return 'Told by a voice rather than shown';
    case 'expounded':
      return 'Explained — a subject treated rather than an event';
    case 'addressed':
      return 'Spoken to the reader';
    case 'exhibited':
      return 'Apparatus shown, with no agent';
    default:
      return '';
  }
}

/** Section id → "Part One › Chapter 3", for the heading above a group of scenes. */
export function sectionPaths(root: SectionNode | undefined): Map<string, string> {
  const paths = new Map<string, string>();

  if (!root) {
    return paths;
  }

  const walk = (node: SectionNode, trail: readonly string[]): void => {
    const here = [...trail, node.title];
    paths.set(node.sectionId, here.join(' › '));

    for (const child of node.children) {
      walk(child, here);
    }
  };

  walk(root, []);
  return paths;
}
