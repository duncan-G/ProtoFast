/** What to ask the model to direct. Single targets replace the author's row; the rest keep edited rows. */
export type DirectTarget =
  | { kind: 'scene' }
  | { kind: 'storyDay' }
  | { kind: 'extras' }
  | { kind: 'continuity' }
  | { kind: 'appearance'; characterId: string }
  | { kind: 'propState'; propId: string }
  | { kind: 'cues' }
  | { kind: 'cue'; elementId: string }
  | { kind: 'frames' }
  | { kind: 'frame'; frameId: string };

/** One string per target, for busy indicators. */
export function targetKey(target: DirectTarget): string {
  switch (target.kind) {
    case 'appearance':
      return `appearance:${target.characterId}`;
    case 'propState':
      return `propState:${target.propId}`;
    case 'cue':
      return `cue:${target.elementId}`;
    case 'frame':
      return `frame:${target.frameId}`;
    default:
      return target.kind;
  }
}

const GROUP: Partial<Record<DirectTarget['kind'], DirectTarget['kind']>> = {
  appearance: 'continuity',
  propState: 'continuity',
  cue: 'cues',
  frame: 'frames',
};

/** Whether directing `outer` also writes what `inner` would. Frames find the scene's extras first. */
export function covers(outer: DirectTarget, inner: DirectTarget): boolean {
  return (
    outer.kind === 'scene' ||
    targetKey(outer) === targetKey(inner) ||
    GROUP[inner.kind] === outer.kind ||
    (outer.kind === 'frames' && inner.kind === 'extras')
  );
}
