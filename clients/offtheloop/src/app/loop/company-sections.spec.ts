import { companySections } from './company-sections';
import { Application } from './model/application';
import { Company } from './model/company';

function company(name: string, lastModified = 0, starred = false): Company {
  return {
    id: name,
    name,
    about: '',
    location: '',
    whatItDoes: '',
    howItMakesMoney: '',
    revenue: '',
    growth: '',
    headcount: '',
    funding: '',
    lastRaised: '',
    values: [],
    practices: [],
    competitors: [],
    concerns: [],
    contacts: [],
    starred,
    lastModified,
  };
}

function application(companyId: string, fields: Partial<Application> = {}): Application {
  return {
    id: `${companyId}-${fields.role ?? ''}`,
    companyId,
    company: companyId,
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
    asked: [],
    learnings: [],
    lastModified: 0,
    ...fields,
  };
}

function names(sections: ReturnType<typeof companySections>) {
  return sections.map((s) => [s.status, s.entries.map((e) => e.company.name)]);
}

describe('company sections', () => {
  it('groups by status, offers first and closed last', () => {
    const companies = ['Brightfield', 'Corvid', 'Halcyon', 'Lumen', 'Northwind', 'Quarry'].map(
      (name) => company(name),
    );
    const applications = [
      application('Corvid', { stage: 'offer', nextOn: '2026-11-12' }),
      application('Halcyon', { stage: 'onsite' }),
      application('Brightfield'),
      application('Northwind', { appliedOn: null }),
      application('Lumen', { stage: 'tech', closed: true }),
    ];

    expect(names(companySections(companies, applications))).toEqual([
      ['offer', ['Corvid']],
      ['active', ['Halcyon']],
      ['applied', ['Brightfield']],
      ['none', ['Northwind', 'Quarry']],
      ['closed', ['Lumen']],
    ]);
  });

  it('sorts a section by stage, then deadline, then the latest change', () => {
    const companies = [
      company('Arbor', 50),
      company('Birch', 10),
      company('Cedar', 30),
      company('Dogwood', 20),
      company('Elm', 40),
    ];
    const applications = [
      application('Arbor', { stage: 'screen' }),
      application('Birch', { stage: 'screen', nextOn: '2026-11-09' }),
      application('Cedar', { stage: 'tech' }),
      application('Dogwood', { stage: 'screen', nextOn: '2026-11-05' }),
      application('Elm', { stage: 'screen', lastModified: 60 }),
    ];

    expect(names(companySections(companies, applications))).toEqual([
      ['active', ['Cedar', 'Dogwood', 'Birch', 'Elm', 'Arbor']],
    ]);
  });

  it('puts starred companies first among those not applied for', () => {
    const companies = [
      company('Arbor', 30),
      company('Birch', 10, true),
      company('Cedar', 20, true),
      company('Dogwood', 40, true),
    ];
    const applications = [
      application('Arbor', { appliedOn: null }),
      application('Dogwood', { stage: 'screen' }),
    ];

    const sections = companySections(companies, applications);

    expect(names(sections)).toEqual([
      ['active', ['Dogwood']],
      ['none', ['Cedar', 'Birch', 'Arbor']],
    ]);
    expect(sections[0].entries[0].starred).toBe(false);
  });

  it('ignores a closed application’s next date', () => {
    const companies = [company('Lumen', 10), company('Vesper', 20)];
    const applications = [
      application('Lumen', { stage: 'tech', closed: true, nextOn: '2026-11-05' }),
      application('Vesper', { stage: 'tech', closed: true }),
    ];

    expect(names(companySections(companies, applications))).toEqual([
      ['closed', ['Vesper', 'Lumen']],
    ]);
  });
});
