import { joinWants, splitWants } from './wants';

describe('splitWants', () => {
  it('moves the ones marked bonus to nice to have, without the mark', () => {
    expect(
      splitWants(['Postgres', 'Search infra (bonus)', 'Go (Nice to have)', 'Kafka (preferred) ']),
    ).toEqual({ must: ['Postgres'], nice: ['Search infra', 'Go', 'Kafka'] });
  });

  it('marks nice-to-haves again when joined', () => {
    expect(joinWants(['Postgres'], ['Go'])).toEqual(['Postgres', 'Go (bonus)']);
  });
});
