import { covers } from './direct-target';

describe('covers', () => {
  it('lets the scene cover every part', () => {
    expect(covers({ kind: 'scene' }, { kind: 'frames' })).toBe(true);
    expect(covers({ kind: 'scene' }, { kind: 'cue', elementId: 'e1' })).toBe(true);
  });

  it('lets a group cover its single items, not the other way round', () => {
    expect(covers({ kind: 'frames' }, { kind: 'frame', frameId: 'p1' })).toBe(true);
    expect(covers({ kind: 'continuity' }, { kind: 'propState', propId: 'x' })).toBe(true);
    expect(covers({ kind: 'frame', frameId: 'p1' }, { kind: 'frames' })).toBe(false);
  });

  it('keeps separate parts apart', () => {
    expect(covers({ kind: 'frames' }, { kind: 'cues' })).toBe(false);
    expect(covers({ kind: 'cue', elementId: 'e1' }, { kind: 'cue', elementId: 'e2' })).toBe(false);
    expect(covers({ kind: 'frames' }, { kind: 'scene' })).toBe(false);
  });
});
