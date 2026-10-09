import { Character } from '../stories/model/character';
import { FrameSpan } from './frames';
import { Frame } from './model/frame';
import { LookChange } from './model/look-change';
import { MediaImage } from './model/media-image';
import { blankPlacement, isBlankPlacement, Placement } from './model/placement';
import { SceneDirection } from './model/scene-direction';

/**
 * A frame follows its scene unless it says otherwise: the scene's shot, angle and movement, who is
 * on screen, what is in view, which wardrobe. Text changes add up instead: the sheet, then the look
 * in force at the frame's last row (the scene's opening look, or the last change at or before it),
 * then what the frame adds. A carried prop is in view exactly when someone carrying it is on screen.
 * Mirrors the API's `FrameContents` and `Looks`.
 */
export type Framing = 'shot' | 'angle' | 'movement';

export type LookSource = 'library' | 'scene' | 'frame';

export interface Inherited<T> {
  value: T;
  /** The frame sets it rather than following the scene. */
  overridden: boolean;
}

export function framingOf(
  direction: SceneDirection,
  frame: Frame | null,
  key: Framing,
): Inherited<string | null> {
  const own = frame?.[key] ?? null;
  return { value: own ?? direction[key], overridden: own !== null };
}

export function onScreenOf(
  direction: SceneDirection,
  frame: Frame | null,
  characterId: string,
): Inherited<boolean> {
  const scene = direction.appearances.find((a) => a.characterId === characterId)?.onScreen ?? true;
  const own = frame?.placements.find((p) => p.characterId === characterId)?.onScreen ?? null;
  return { value: own ?? scene, overridden: own !== null };
}

/** Element id to its index in the scene. */
export type ElementOrder = ReadonlyMap<string, number>;

/** A character's look at one point of the scene, with the change that sets it, if any. */
export interface LookAt {
  wardrobeId: string | null;
  wardrobeChange: string;
  appearanceChange: string;
  holding: string[];
  /** Null while the scene's opening look holds. */
  change: LookChange | null;
  image: MediaImage | null;
}

/** The character's changes in line order; changes on lines no longer in the scene sort last. */
export function changesOf(
  direction: SceneDirection,
  characterId: string,
  order: ElementOrder,
): LookChange[] {
  const at = (c: LookChange) => order.get(c.elementId) ?? Number.MAX_SAFE_INTEGER;
  return direction.lookChanges
    .filter((c) => c.characterId === characterId)
    .sort((a, b) => at(a) - at(b));
}

/** The look in force at row `at`; null is the look the scene opens with. */
export function lookAt(
  direction: SceneDirection,
  characterId: string,
  order: ElementOrder,
  at: number | null,
): LookAt {
  const change =
    at === null
      ? null
      : (changesOf(direction, characterId, order)
          .filter((c) => (order.get(c.elementId) ?? Number.MAX_SAFE_INTEGER) <= at)
          .at(-1) ?? null);
  const row = change ?? direction.appearances.find((a) => a.characterId === characterId);
  return {
    wardrobeId: row?.wardrobeId ?? null,
    wardrobeChange: row?.wardrobeChange ?? '',
    appearanceChange: row?.appearanceChange ?? '',
    holding: row?.holding ?? [],
    change,
    image: row?.image ?? null,
  };
}

/** Props someone carries in the frame, each with whether one of its holders is on screen. */
export function heldIn(
  direction: SceneDirection,
  span: FrameSpan,
  order: ElementOrder,
): Map<string, boolean> {
  const held = new Map<string, boolean>();
  const people = new Set([
    ...direction.appearances.map((a) => a.characterId),
    ...direction.lookChanges.map((c) => c.characterId),
  ]);
  for (const id of people) {
    const on = onScreenOf(direction, span.frame, id).value;
    for (const prop of lookAt(direction, id, order, span.last).holding) {
      held.set(prop, (held.get(prop) ?? false) || on);
    }
  }
  return held;
}

/** `held` comes from `heldIn` for the frame; without it a carried prop follows the scene. */
export function inViewOf(
  direction: SceneDirection,
  frame: Frame | null,
  propId: string,
  held?: ReadonlyMap<string, boolean>,
): Inherited<boolean> {
  const scene =
    (frame ? held?.get(propId) : undefined) ??
    direction.propStates.find((p) => p.propId === propId)?.inView ??
    true;
  const own = frame?.props.find((p) => p.propId === propId)?.inView ?? null;
  return { value: own ?? scene, overridden: own !== null };
}

export function extraOnScreenOf(
  direction: SceneDirection,
  frame: Frame | null,
  extraId: string,
): Inherited<boolean> {
  const scene = direction.extras.find((e) => e.extraId === extraId)?.onScreen ?? true;
  const own = frame?.extras.find((e) => e.extraId === extraId)?.onScreen ?? null;
  return { value: own ?? scene, overridden: own !== null };
}

/** The sheet's first wardrobe is its default; `look` is the look in force at the frame. */
export function wardrobeOf(
  direction: SceneDirection,
  frame: Frame | null,
  character: Character,
  look?: LookAt,
): { wardrobeId: string | null; source: LookSource } {
  const own = frame?.placements.find((p) => p.characterId === character.id)?.wardrobeId ?? null;
  if (own !== null) {
    return { wardrobeId: own, source: 'frame' };
  }
  const scene = look
    ? look.wardrobeId
    : direction.appearances.find((a) => a.characterId === character.id)?.wardrobeId;
  if (scene) {
    return { wardrobeId: scene, source: 'scene' };
  }
  return { wardrobeId: character.wardrobes[0]?.id ?? null, source: 'library' };
}

/** The non-empty parts in order, as one phrase. */
export function joinChanges(...parts: (string | null | undefined)[]): string {
  return parts
    .map((p) => p?.trim() ?? '')
    .filter(Boolean)
    .join(' ');
}

/** The frame's placement for a character with a patch applied; a blank one is dropped. */
export function withPlacement(
  frame: Frame,
  characterId: string,
  patch: Partial<Placement>,
): Placement[] {
  const current =
    frame.placements.find((p) => p.characterId === characterId) ?? blankPlacement(characterId);
  const next = { ...current, ...patch };
  const rest = frame.placements.filter((p) => p.characterId !== characterId);
  return isBlankPlacement(next) ? rest : [...rest, next];
}

/** How many of a frame's fields it sets itself, across framing, cast, props and looks. */
export function overrideCount(frame: Frame): number {
  const framing = (['shot', 'angle', 'movement'] as const).filter((k) => frame[k] !== null).length;
  const cast = frame.placements.some((p) => p.onScreen !== null) ? 1 : 0;
  const props = frame.props.length > 0 ? 1 : 0;
  const looks = frame.placements.filter(
    (p) => p.wardrobeId !== null || p.wardrobeChange || p.appearanceChange,
  ).length;
  return framing + cast + props + looks + (frame.locationChange ? 1 : 0);
}
