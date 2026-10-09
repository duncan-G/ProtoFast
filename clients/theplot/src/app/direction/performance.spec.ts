import {
  assemble,
  insertTag,
  reanchor,
  removeTag,
  sameWords,
  stripTags,
  tagsOf,
  tokenize,
} from './performance';

describe('performance tags', () => {
  it('reads tags and words apart', () => {
    expect(tagsOf('[Sad] I never [sighs] do.')).toEqual(['sad', 'sighs']);
    expect(stripTags('[sad] I never [sighs] do.')).toBe('I never do.');
    expect(tokenize('[sad]I never [sighs] do.')).toEqual([
      { kind: 'tag', text: 'sad' },
      { kind: 'word', text: 'I' },
      { kind: 'word', text: 'never' },
      { kind: 'tag', text: 'sighs' },
      { kind: 'word', text: 'do.' },
    ]);
  });

  it('compares words without tags or pause punctuation', () => {
    expect(sameWords('[sad] I… I never do—', 'I, I never do.')).toBe(true);
    expect(sameWords('[sad] I never do.', 'I never did.')).toBe(false);
  });

  it('assembles a performance from the line and tag placements, capped at three tags', () => {
    expect(assemble("Don't move.", ['Whispers'], [{ beforeWord: 1, tag: 'pause' }])).toBe(
      "[whispers] Don't [pause] move.",
    );
    expect(assemble('a b', ['x', 'y', 'z'], [{ beforeWord: 1, tag: 'w' }])).toBe('[x] [y] [z] a b');
    expect(assemble('a b', ['x', 'x'], [{ beforeWord: 9, tag: 'end' }])).toBe('[x] a b [end]');
  });

  it('inserts and removes tags in place', () => {
    expect(insertTag("Don't move.", 0, 'sad')).toBe("[sad] Don't move.");
    expect(insertTag("[sad] Don't move.", 1, 'pause')).toBe("[sad] Don't [pause] move.");
    expect(insertTag("[sad] Don't move.", 2, 'sighs')).toBe("[sad] Don't move. [sighs]");
    expect(insertTag('[a] [b] [c] x', 0, 'd')).toBeNull();
    expect(insertTag('[a] x', 0, 'a')).toBeNull();
    expect(removeTag("[sad] Don't [pause] move.", 'pause')).toBe("[sad] Don't move.");
  });

  it('carries tags onto a changed line by anchor', () => {
    expect(reanchor("[sad] I'm sorry.", "Max, I'm really sorry.")).toBe(
      "[sad] Max, I'm really sorry.",
    );
    expect(reanchor('I never [sighs] do.', 'I never ever do.')).toBe('I never ever [sighs] do.');
    expect(reanchor('I never [sighs] do.', 'Never.')).toBe('Never.');
    expect(reanchor('Go. [laughs]', 'Go now.')).toBe('Go now. [laughs]');
  });
});
