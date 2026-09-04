/** A line in the hero's live build console — one agent, one job, one state. */
export interface AgentEvent {
  agent: string;
  detail: string;
  /** `done` is finished and dimmed, `live` pulses, `next` is queued and greyed. */
  state: 'done' | 'live' | 'next';
}

export interface Step {
  index: string;
  title: string;
  description: string;
}

/** One of the six "real product, not a mockup" cards. */
export interface Feature {
  kicker: string;
  title: string;
  body: string;
}

/** A mark on the half-hour timeline. */
export interface Milestone {
  time: string;
  title: string;
  detail: string;
}

export interface Stat {
  value: string;
  label: string;
}

export interface PricingTier {
  name: string;
  tagline: string;
  price: string;
  period: string;
  features: string[];
  cta: string;
  popular: boolean;
}

/** Example prompts the hero field types through. */
export const LANDING_PROMPTS = [
  'a booking app for my dog grooming business, with deposits',
  'a members-only recipe club that bills monthly',
  'an invoicing tool for freelance photographers',
  'a waitlist app for my supper club, with table assignments',
];

/**
 * The build the hero console is showing. Fixed sample data, not a live feed —
 * it is a picture of the product, and the `live` rows are what the glow
 * animation attaches to.
 */
export const LANDING_AGENT_FEED: AgentEvent[] = [
  { agent: 'Architect', detail: 'Schema — 9 tables, relations locked', state: 'done' },
  { agent: 'UI', detail: 'Booking calendar component', state: 'done' },
  { agent: 'Payments', detail: 'Deploying webhooks…', state: 'live' },
  { agent: 'Auth', detail: 'Sessions + password reset', state: 'live' },
  { agent: 'Deploy', detail: 'Queued — TLS + shareable URL', state: 'next' },
];

export const LANDING_STATS: Stat[] = [
  { value: '< 30 min', label: 'Idea to production URL' },
  { value: 'Built in', label: 'Auth, payments, hosting' },
  { value: 'Web + mobile', label: 'From one description' },
];

export const LANDING_STEPS: Step[] = [
  {
    index: '01',
    title: 'Describe it',
    description:
      'Explain it like you would to a developer friend. No specs, no wireframes, no tickets — the sentence is the interface.',
  },
  {
    index: '02',
    title: 'Agents build it',
    description:
      'Data model, API, UI, auth and payments get split across specialised agents working in parallel — and you watch every decision land.',
  },
  {
    index: '03',
    title: "It's live",
    description:
      'Production hosting, a shareable URL, sign-in that works and payments that can charge a real card. Same half hour.',
  },
];

export const LANDING_FEATURES: Feature[] = [
  {
    kicker: 'Parallel',
    title: 'A swarm, not a queue',
    body: "Architecture, UI, API and infra agents build concurrently. That's why minutes, not months.",
  },
  {
    kicker: 'Accounts',
    title: 'Auth out of the box',
    body: "Sign-up, sessions, password reset — wired from the first build. Testers log in like it's a real product.",
  },
  {
    kicker: 'Revenue',
    title: 'Payments built in',
    body: 'Subscriptions, checkout and billing pre-integrated. Flip off test mode and it takes real money.',
  },
  {
    kicker: 'Reach',
    title: 'Web and mobile, one prompt',
    body: 'Every build ships a responsive web app and a mobile-ready experience together.',
  },
  {
    kicker: 'Hosting',
    title: 'Production from minute one',
    body: 'No localhost, no staging purgatory. Managed hosting with TLS the moment the agents finish.',
  },
  {
    kicker: 'Iteration',
    title: 'Change it by chatting',
    body: '"Make the dashboard dark." "Add referrals." Live in minutes — feedback loops measured in coffee sips.',
  },
];

export const LANDING_MILESTONES: Milestone[] = [
  {
    time: '0:00',
    title: 'You hit Prototype it',
    detail: 'The planning agent drafts the architecture.',
  },
  {
    time: '0:02',
    title: 'The swarm fans out',
    detail: 'UI, API, data, auth, payments in parallel.',
  },
  {
    time: '0:12',
    title: 'First working preview',
    detail: 'Click real screens while the backend wires up.',
  },
  {
    time: '0:22',
    title: 'Auth and payments green',
    detail: 'Accounts sign in, test cards charge, hooks fire.',
  },
  {
    time: '0:28',
    title: 'Deployed to production',
    detail: 'A shareable URL with TLS, ready for users.',
  },
];

export const LANDING_PRICING_TIERS: PricingTier[] = [
  {
    name: 'Starter',
    tagline: 'For trying the trick yourself.',
    price: '$0',
    period: 'forever',
    features: [
      '3 prototypes per month',
      'Web + mobile output',
      'Auth included',
      'Test-mode payments',
      'protofast.app subdomain',
    ],
    cta: 'Start free',
    popular: false,
  },
  {
    name: 'Pro',
    tagline: 'For founders shipping for real.',
    price: '$49',
    period: 'per month',
    features: [
      'Unlimited prototypes',
      'Live payments — keep 100%',
      'Custom domains',
      'Iterate by chat on live apps',
      'Export the full source',
    ],
    cta: 'Go Pro',
    popular: true,
  },
  {
    name: 'Team',
    tagline: 'For agencies and product teams.',
    price: '$199',
    period: 'per month',
    features: [
      'Everything in Pro',
      '5 seats, shared workspaces',
      'White-label client previews',
      'Priority agent capacity',
      'SSO and audit logs',
    ],
    cta: 'Start a team',
    popular: false,
  },
];
