/** What to ask the model to direct. Single targets replace the author's row; the rest keep edited rows. */
export type DirectTarget =
  | { kind: 'scene' }
  | { kind: 'storyDay' }
  | { kind: 'continuity' }
  | { kind: 'appearance'; characterId: string }
  | { kind: 'propState'; propId: string }
  | { kind: 'cues' }
  | { kind: 'cue'; elementId: string }
  | { kind: 'panels' }
  | { kind: 'panel'; panelId: string };

/** One string per target, for busy indicators. */
export function targetKey(target: DirectTarget): string {
  switch (target.kind) {
    case 'appearance':
      return `appearance:${target.characterId}`;
    case 'propState':
      return `propState:${target.propId}`;
    case 'cue':
      return `cue:${target.elementId}`;
    case 'panel':
      return `panel:${target.panelId}`;
    default:
      return target.kind;
  }
}
