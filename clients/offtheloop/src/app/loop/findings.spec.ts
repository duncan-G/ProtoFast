import { changesFrom, companyFindings, otherEmployer, postingFindings } from './findings';
import { Company } from './model/company';
import { PostingFindings } from './model/posting-findings';

const COMPANY: Company = {
  id: 'c1',
  name: 'Halcyon Pay',
  about: '',
  location: '',
  whatItDoes: '',
  howItMakesMoney: '',
  revenue: '$120M',
  growth: '',
  headcount: '~400',
  funding: '',
  lastRaised: '',
  values: ['Default to writing'],
  practices: [],
  competitors: [],
  concerns: [],
  contacts: [],
  lastModified: 0,
};

const NOTHING = {
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
};

const POSTING: PostingFindings = {
  application: {
    role: 'Senior SWE',
    postingUrl: 'https://jobs.example/1',
    posting: 'The whole posting',
    postedPay: '$180–220k',
    team: '',
    reportsTo: '',
    arrangement: 'hybrid',
    location: '',
    hours: '',
    sponsorsVisa: false,
    wants: ['Go'],
    stack: [],
    benefits: [],
    questions: [],
  },
  company: { about: 'fintech · payments', values: ['Ownership'], practices: [] },
  employer: 'Halcyon',
  sources: [],
};

describe('findings', () => {
  it('ticks what fills a blank, leaves replacements unticked, and skips what is known', () => {
    const findings = companyFindings(COMPANY, {
      ...NOTHING,
      about: 'fintech · payments api',
      whatItDoes: 'Runs payments for online shops.',
      revenue: '$140M',
      headcount: '~ 400',
      values: ['default to writing', 'Ownership'],
    });

    expect(findings.map((f) => [f.key, f.text, f.replaces, f.keep])).toEqual([
      ['about', 'fintech · payments api', '', true],
      ['whatItDoes', 'Runs payments for online shops.', '', true],
      ['revenue', '$140M', '$120M', false],
      ['values', 'Ownership', '', true],
    ]);
  });

  it('turns kept findings into changes, appending list items', () => {
    const findings = companyFindings(COMPANY, {
      ...NOTHING,
      revenue: '$140M',
      values: ['Ownership', 'Craft'],
    });
    findings[0].keep = true;
    findings[2].keep = false;

    expect(changesFrom(COMPANY, findings, 'company')).toEqual({
      revenue: '$140M',
      values: ['Default to writing', 'Ownership'],
    });
  });

  it('offers executives not already among the people, and appends the kept ones', () => {
    const company = {
      ...COMPANY,
      contacts: [{ name: 'Ada Park', role: 'CEO', metOn: '2026-10-01', note: 'first call' }],
    };
    const findings = companyFindings(company, {
      ...NOTHING,
      contacts: [
        { name: 'ada park', role: 'CEO · co-founder', metOn: null, note: '' },
        { name: 'Sam Ortiz', role: 'CTO', metOn: null, note: '' },
        { name: 'Lee Wu', role: '', metOn: null, note: '' },
      ],
    });

    expect(findings.map((f) => [f.label, f.text, f.keep])).toEqual([
      ['executives', 'Sam Ortiz · CTO', true],
      ['executives', 'Lee Wu', true],
    ]);
    findings[1].keep = false;
    expect(changesFrom(company, findings, 'company')).toEqual({
      contacts: [company.contacts[0], { name: 'Sam Ortiz', role: 'CTO', metOn: null, note: '' }],
    });
  });

  it('reads a posting for a new application as all new, with the company facts beside it', () => {
    const findings = postingFindings(null, COMPANY, POSTING);

    expect(findings.every((f) => f.keep)).toBe(true);
    expect(findings.find((f) => f.key === 'sponsorsVisa')?.text).toBe("doesn't sponsor");
    expect(findings.find((f) => f.key === 'posting')?.long).toBe(true);
    expect(changesFrom(COMPANY, findings, 'company')).toEqual({
      about: 'fintech · payments',
      values: ['Default to writing', 'Ownership'],
    });
  });

  it('notices a posting for another employer', () => {
    expect(otherEmployer('Halcyon', 'Halcyon Pay')).toBe(false);
    expect(otherEmployer('Northwind Labs', 'Halcyon Pay')).toBe(true);
    expect(otherEmployer('', 'Halcyon Pay')).toBe(false);
  });
});
