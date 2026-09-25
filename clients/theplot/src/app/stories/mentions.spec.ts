import {
  applyTyping,
  editedRange,
  insertReference,
  linkTypedReferences,
  matchReference,
  mentionTarget,
  renameReferences,
  forgetReferences,
  segmentText,
} from './mentions';
import { MentionedText } from './model/mentioned-text';
import { Referable } from './model/referable';

const MARA: Referable = { kind: 'character', id: 'c-mara', name: 'Mara' };
const OLD_FEN: Referable = { kind: 'character', id: 'c-fen', name: 'Old Fen' };
const FEN: Referable = { kind: 'character', id: 'c-fen2', name: 'Fen' };
const RADIO: Referable = { kind: 'prop', id: 'p-radio', name: 'Radio' };
const RADIO_VOICE: Referable = {
  kind: 'character',
  id: 'c-voice',
  name: 'Radio Voice',
};
const HARBOR_CHARACTER: Referable = {
  kind: 'character',
  id: 'c-harbor',
  name: 'Harbor',
};
const HARBOR_LOCATION: Referable = {
  kind: 'location',
  id: 'l-harbor',
  name: 'Harbor',
};
const LIBRARY = [MARA, OLD_FEN, FEN, RADIO, RADIO_VOICE, HARBOR_CHARACTER, HARBOR_LOCATION];

const linked = (text: string, library = LIBRARY): MentionedText =>
  linkTypedReferences({ text, mentions: [] }, library, 0, text.length);

const spans = (value: MentionedText) =>
  value.mentions.map((m) => [value.text.slice(m.offset, m.offset + m.length), mentionTarget(m).id]);

describe('matchReference', () => {
  it('prefers the longest name', () => {
    expect(matchReference('@Old Fen nods', 0, LIBRARY)).toBe(OLD_FEN);
    expect(matchReference('@Radio Voice hums', 0, LIBRARY)).toBe(RADIO_VOICE);
    expect(matchReference('@Radio hums', 0, LIBRARY)).toBe(RADIO);
  });

  it('ignores case', () => {
    expect(matchReference('@mara runs', 0, LIBRARY)).toBe(MARA);
  });

  it('needs the name to end on a word boundary', () => {
    expect(matchReference('@Marathon', 0, LIBRARY)).toBeNull();
    expect(matchReference('@Mara’s radio', 0, LIBRARY)).toBe(MARA);
    expect(matchReference('@Mara.', 0, LIBRARY)).toBe(MARA);
  });

  it('skips an @ inside a word', () => {
    expect(matchReference('mail@Mara', 4, LIBRARY)).toBeNull();
  });

  it('breaks a character and location tie toward the character, as the autocomplete lists them', () => {
    expect(matchReference('@Harbor at night', 0, LIBRARY)).toBe(HARBOR_CHARACTER);
  });
});

describe('editedRange', () => {
  it('finds a plain insertion', () => {
    expect(editedRange('ab', 'aXb')).toEqual({
      start: 1,
      oldEnd: 1,
      newEnd: 2,
    });
  });

  it('uses the caret when repeated characters make the split ambiguous', () => {
    expect(editedRange('aa', 'aaa', 1)).toEqual({
      start: 0,
      oldEnd: 0,
      newEnd: 1,
    });
    expect(editedRange('aa', 'aaa')).toEqual({
      start: 2,
      oldEnd: 2,
      newEnd: 3,
    });
  });

  it('finds a deletion and a replacement', () => {
    expect(editedRange('abc', 'ac', 1)).toEqual({
      start: 1,
      oldEnd: 2,
      newEnd: 1,
    });
    expect(editedRange('a big day', 'a small day', 7)).toEqual({
      start: 2,
      oldEnd: 5,
      newEnd: 7,
    });
  });
});

describe('applyTyping', () => {
  const start = linked('Then @Mara takes the @Radio.');

  it('shifts mentions after an edit before them', () => {
    const after = applyTyping(start, 'And then @Mara takes the @Radio.', LIBRARY, 4);
    expect(spans(after)).toEqual([
      ['@Mara', 'c-mara'],
      ['@Radio', 'p-radio'],
    ]);
    expect(after.mentions.map((m) => m.offset)).toEqual([9, 25]);
  });

  it('keeps mentions before an edit where they were', () => {
    const after = applyTyping(start, 'Then @Mara takes the @Radio. Static.', LIBRARY);
    expect(after.mentions.map((m) => m.offset)).toEqual([5, 21]);
  });

  it('keeps a mention when typing right before its @ or right after its name', () => {
    const before = applyTyping(start, 'Then  @Mara takes the @Radio.', LIBRARY, 5);
    expect(spans(before)[0]).toEqual(['@Mara', 'c-mara']);
    const after = applyTyping(start, 'Then @Mara, takes the @Radio.', LIBRARY, 11);
    expect(spans(after)[0]).toEqual(['@Mara', 'c-mara']);
  });

  it('drops a mention the edit cuts into and leaves its text plain', () => {
    const after = applyTyping(start, 'Then @Mxara takes the @Radio.', LIBRARY, 7);
    expect(spans(after)).toEqual([['@Radio', 'p-radio']]);
    expect(after.text).toContain('@Mxara');
  });

  it('drops a mention deleted with its text and shifts the rest back', () => {
    const after = applyTyping(start, 'Then takes the @Radio.', LIBRARY, 5);
    expect(spans(after)).toEqual([['@Radio', 'p-radio']]);
    expect(after.mentions[0].offset).toBe(15);
  });

  it('links a typed name once a boundary follows it', () => {
    let value: MentionedText = { text: 'Enter @Mara', mentions: [] };
    value = applyTyping(value, 'Enter @Mara', LIBRARY, 11, 6);
    expect(value.mentions).toEqual([]);
    value = applyTyping(value, 'Enter @Mara ', LIBRARY, 12);
    expect(spans(value)).toEqual([['@Mara', 'c-mara']]);
  });

  it('does not link a name that runs on into a longer word', () => {
    const value = applyTyping({ text: '@Marat', mentions: [] }, '@Marat ', LIBRARY, 7);
    expect(value.mentions).toEqual([]);
  });

  it('does not relink an unlinked name the edit did not touch', () => {
    const plain: MentionedText = { text: '@Mara waits. ', mentions: [] };
    const value = applyTyping(plain, '@Mara waits. Then', LIBRARY, 17);
    expect(value.mentions).toEqual([]);
  });
});

describe('insertReference', () => {
  it('replaces the typed query and links the picked target, even on a name tie', () => {
    const value: MentionedText = { text: 'Out on the @Har', mentions: [] };
    const { value: next, caret } = insertReference(value, 11, 15, HARBOR_LOCATION);
    expect(next.text).toBe('Out on the @Harbor ');
    expect(spans(next)).toEqual([['@Harbor', 'l-harbor']]);
    expect(caret).toBe(19);
  });

  it('adds a space after a word it follows and moves later mentions', () => {
    const value = linked('She waits for @Mara');
    const { value: next } = insertReference(value, 3, 3, RADIO);
    expect(next.text).toBe('She @Radio waits for @Mara');
    expect(spans(next)).toEqual([
      ['@Radio', 'p-radio'],
      ['@Mara', 'c-mara'],
    ]);
  });
});

describe('renameReferences', () => {
  it('rewrites each mention of the target and shifts the ones after it', () => {
    const value = linked('@Mara hands @Old Fen the key. @Mara leaves.');
    const next = renameReferences(value, { kind: 'character', id: 'c-mara' }, 'Mara Voss');
    expect(next.text).toBe('@Mara Voss hands @Old Fen the key. @Mara Voss leaves.');
    expect(spans(next)).toEqual([
      ['@Mara Voss', 'c-mara'],
      ['@Old Fen', 'c-fen'],
      ['@Mara Voss', 'c-mara'],
    ]);
  });
});

describe('forgetReferences', () => {
  it('unlinks the target but leaves its text', () => {
    const value = linked('@Mara hands over the @Radio.');
    const mentions = forgetReferences(value.mentions, {
      kind: 'prop',
      id: 'p-radio',
    });
    const segments = segmentText({ text: value.text, mentions });
    expect(segments.map((s) => [s.text, s.mention !== null])).toEqual([
      ['@Mara', true],
      [' hands over the @Radio.', false],
    ]);
  });
});
