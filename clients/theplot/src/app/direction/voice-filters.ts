/**
 * What a sheet says about a character, as the speech provider's library labels. Mirrors
 * `Services/Directing/VoiceFilters.cs`, which casts with the same rules.
 */

const ACCENT_WORDS: [string, string[]][] = [
  ['american', ['american', 'texan', 'southern', 'new york', 'brooklyn']],
  [
    'british',
    ['british', 'english', 'london', 'cockney', 'rp', 'received pronunciation', 'yorkshire'],
  ],
  ['australian', ['australian', 'aussie']],
  ['canadian', ['canadian']],
  ['irish', ['irish', 'dublin']],
  ['scottish', ['scottish', 'scots', 'glaswegian', 'glasgow', 'edinburgh', 'highland']],
  ['indian', ['indian']],
];

export function genderLabel(gender: string | null | undefined): string {
  const text = (gender ?? '').toLowerCase();
  if (matches(text, 'non-binary', 'nonbinary', 'neutral', 'agender', 'genderless')) {
    return 'neutral';
  }
  if (matches(text, 'female', 'woman', 'girl', 'f')) {
    return 'female';
  }
  return matches(text, 'male', 'man', 'boy', 'm') ? 'male' : '';
}

/** "young" under 30, "middle_aged" under 60, "old" from there; words where there is no number. */
export function ageLabel(age: string | null | undefined): string {
  const text = (age ?? '').toLowerCase();
  if (text.includes('middle')) {
    return 'middle_aged';
  }
  const number = /\d{1,3}/.exec(text);
  if (number) {
    const years = Number(number[0]);
    return years < 30 ? 'young' : years < 60 ? 'middle_aged' : 'old';
  }
  if (
    matches(text, 'child', 'kid', 'teen', 'teenager', 'teenage', 'young', 'youth', 'adolescent')
  ) {
    return 'young';
  }
  return matches(text, 'old', 'elderly', 'aged', 'senior', 'ancient') ? 'old' : '';
}

/** The accent named first in the texts, or empty. */
export function accentLabel(...texts: (string | null | undefined)[]): string {
  const text = texts.filter(Boolean).join(' ').toLowerCase();
  let best: { accent: string; at: number } | null = null;
  for (const [accent, words] of ACCENT_WORDS) {
    for (const word of words) {
      const at = find(text, word);
      if (at >= 0 && (best === null || at < best.at)) {
        best = { accent, at };
      }
    }
  }
  return best?.accent ?? '';
}

function matches(text: string, ...words: string[]): boolean {
  return words.some((w) => find(text, w) >= 0);
}

function find(text: string, word: string): number {
  const escaped = word.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return new RegExp(`(?<![a-z])${escaped}(?![a-z])`).exec(text)?.index ?? -1;
}
