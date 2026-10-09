import { Character } from '../stories/model/character';
import { Frame } from './model/frame';
import { blankPlacement, isBlankPlacement, Placement } from './model/placement';
import { SceneDirection } from './model/scene-direction';

/**
 * A frame follows its scene unless it says otherwise: the scene's shot, angle and movement, who is
 * on screen, what is in view, which wardrobe. Text changes add up instead: the sheet, then what
 * the scene changes, then what the frame adds.
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

export function inViewOf(
  direction: SceneDirection,
  frame: Frame | null,
  propId: string,
): Inherited<boolean> {
  const scene = direction.propStates.find((p) => p.propId === propId)?.inView ?? true;
  const own = frame?.props.find((p) => p.propId === propId)?.inView ?? null;
  return { value: own ?? scene, overridden: own !== null };
}

/** The sheet's first wardrobe is its default. */
export function wardrobeOf(
  direction: SceneDirection,
  frame: Frame | null,
  character: Character,
): { wardrobeId: string | null; source: LookSource } {
  const own = frame?.placements.find((p) => p.characterId === character.id)?.wardrobeId ?? null;
  if (own !== null) {
    return { wardrobeId: own, source: 'frame' };
  }
  const scene = direction.appearances.find((a) => a.characterId === character.id)?.wardrobeId;
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
