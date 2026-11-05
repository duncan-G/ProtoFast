import { glanceLine, stageName, stageSegments } from './application-glance';

describe('application at a glance', () => {
  const base = {
    stage: 'applied' as const,
    closed: false,
    appliedOn: '2026-11-01' as string | null,
    nextStep: '',
    nextOn: null as string | null,
  };

  it('says what is next, how long it has waited, or how it ended', () => {
    expect(glanceLine(base, '2026-11-04')).toBe('No reply yet · 3 days');
    expect(glanceLine({ ...base, appliedOn: null }, '2026-11-04')).toBe('Not applied yet');
    expect(
      glanceLine(
        { ...base, stage: 'onsite', nextStep: 'Onsite loop', nextOn: '2026-11-04' },
        '2026-11-04',
      ),
    ).toBe('Onsite loop · today');
    expect(glanceLine({ ...base, stage: 'tech', closed: true }, '2026-11-04')).toBe(
      'Closed at tech',
    );
  });

  it('lights the stage under way and darkens a closed one', () => {
    expect(stageSegments({ ...base, stage: 'tech' })).toEqual([
      'var(--color-text-2)',
      'var(--color-text-2)',
      'var(--color-amber)',
      'var(--color-line-2)',
      'var(--color-line-2)',
    ]);
    expect(stageSegments({ ...base, stage: 'screen', closed: true }).slice(0, 3)).toEqual([
      'var(--color-edge-2)',
      'var(--color-edge-2)',
      'var(--color-line-2)',
    ]);
    expect(stageSegments({ ...base, appliedOn: null })[0]).toBe('var(--color-line-2)');
    expect(stageName({ ...base, appliedOn: null })).toBe('Not applied');
  });
});
