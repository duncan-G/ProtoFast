import { Application } from './model/application';
import { nextLabel, roundsAhead, searchDay, upcoming } from './loop-summary';

const NOW = '2026-11-03';

function application(company: string, fields: Partial<Application> = {}): Application {
  return {
    id: company,
    companyId: `c-${company}`,
    company,
    role: '',
    stage: 'applied',
    closed: false,
    appliedOn: '2026-10-14',
    source: null,
    sourceDetail: '',
    resume: '',
    resumeId: null,
    cvId: null,
    postingUrl: '',
    posting: '',
    nextStep: '',
    nextOn: null,
    postedPay: '',
    askedPay: '',
    offeredPay: '',
    offerTerms: '',
    wants: [],
    questions: [],
    team: '',
    reportsTo: '',
    stack: [],
    arrangement: null,
    location: '',
    hours: '',
    sponsorsVisa: null,
    benefits: [],
    rounds: [],
    learned: '',
    lastModified: 0,
    ...fields,
  };
}

describe('loop summary', () => {
  const loop = [
    application('Corvid Games', { stage: 'offer', nextOn: '2026-11-12', appliedOn: '2026-09-27' }),
    application('Halcyon Pay', { stage: 'onsite', nextOn: '2026-11-06' }),
    application('Quarry Systems', { stage: 'screen', nextOn: '2026-11-04' }),
    application('Lumen Health', { stage: 'tech', closed: true, nextOn: '2026-11-05' }),
    application('Brightfield'),
  ];

  it('counts the search from the first application', () => {
    expect(searchDay(loop, NOW)).toBe(38);
    expect(searchDay([], NOW)).toBeNull();
    expect(searchDay([application('Northwind Labs', { appliedOn: null })], NOW)).toBeNull();
  });

  it('puts each open next step on its day, offers apart', () => {
    const days = upcoming(loop, NOW, 10);
    expect(days[1].events).toEqual([
      {
        applicationId: 'Quarry Systems',
        companyId: 'c-Quarry Systems',
        company: 'Quarry',
        what: 'screen',
        offer: false,
      },
    ]);
    expect(days[2].events).toEqual([]);
    expect(days[9].events[0]).toMatchObject({ company: 'Corvid', what: 'offer ends', offer: true });
    expect(roundsAhead(loop, NOW, 10)).toBe(2);
  });

  it('labels the next step', () => {
    expect(nextLabel(loop[0], NOW)).toBe('9d left');
    expect(nextLabel(loop[1], NOW)).toBe('fri');
    expect(nextLabel(loop[3], NOW)).toBe('');
    expect(nextLabel(loop[4], NOW)).toBe('—');
  });
});
