import { figureParts } from './figure';

describe('figureParts', () => {
  it('moves bracketed context off the figure', () => {
    expect(figureParts('$8.4B (content sales/licensing, FY2025)')).toEqual({
      figure: '$8.4B',
      context: 'content sales/licensing, FY2025',
    });
    expect(figureParts(' ~2,400 (~231k parent company) ')).toEqual({
      figure: '~2,400',
      context: '~231k parent company',
    });
  });

  it('takes only the last brackets, and leaves a figure without them whole', () => {
    expect(figureParts('$5M (est.) (2024)')).toEqual({ figure: '$5M (est.)', context: '2024' });
    expect(figureParts('Series C · $210M')).toEqual({ figure: 'Series C · $210M', context: '' });
    expect(figureParts('(private)')).toEqual({ figure: '(private)', context: '' });
    expect(figureParts('')).toEqual({ figure: '', context: '' });
  });
});
