import { accentLabel, ageLabel, genderLabel } from './voice-filters';

describe('voice filters', () => {
  it('reads gender words, not the letters inside them', () => {
    expect(genderLabel('Female')).toBe('female');
    expect(genderLabel('a man')).toBe('male');
    expect(genderLabel('woman')).toBe('female');
    expect(genderLabel('Non-binary')).toBe('neutral');
    expect(genderLabel('')).toBe('');
  });

  it('buckets ages by number first, then by words', () => {
    expect(ageLabel('late 50s')).toBe('middle_aged');
    expect(ageLabel('62')).toBe('old');
    expect(ageLabel('19')).toBe('young');
    expect(ageLabel('middle-aged')).toBe('middle_aged');
    expect(ageLabel('elderly')).toBe('old');
    expect(ageLabel('teenager')).toBe('young');
    expect(ageLabel('ageless')).toBe('');
  });

  it('takes the accent named first', () => {
    expect(accentLabel('low, quick, Glaswegian')).toBe('scottish');
    expect(accentLabel('English, then a Dublin lilt')).toBe('british');
    expect(accentLabel('warm', null)).toBe('');
  });
});
