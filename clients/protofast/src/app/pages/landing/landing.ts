import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { ImagePlaceholder } from './image-placeholder';
import { ProtofastLogo } from '../../shared/protofast-logo';
import { AuthIdentityService } from '../../auth/auth-identity';

/** A line in the hero's live build console — one agent, one job, one state. */
interface AgentEvent {
  agent: string;
  detail: string;
  /** `done` is finished and dimmed, `live` pulses, `next` is queued and greyed. */
  state: 'done' | 'live' | 'next';
}

interface Step {
  index: string;
  title: string;
  description: string;
}

/** One of the six "real product, not a mockup" cards. */
interface Feature {
  kicker: string;
  title: string;
  body: string;
}

/** A mark on the half-hour timeline. */
interface Milestone {
  time: string;
  title: string;
  detail: string;
}

interface Stat {
  value: string;
  label: string;
}

interface PricingTier {
  name: string;
  tagline: string;
  price: string;
  period: string;
  features: string[];
  cta: string;
  popular: boolean;
}

@Component({
  selector: 'app-landing',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ImagePlaceholder, ProtofastLogo, RouterLink],
  templateUrl: './landing.html',
})
export class Landing {
  protected readonly auth = inject(AuthIdentityService);
  private readonly destroyRef = inject(DestroyRef);

  /** Example prompts the hero field types through. */
  private readonly prompts = [
    'a booking app for my dog grooming business, with deposits',
    'a members-only recipe club that bills monthly',
    'an invoicing tool for freelance photographers',
    'a waitlist app for my supper club, with table assignments',
  ];

  /**
   * The hero prompt text. Seeded with the first prompt in full so SSR renders a
   * complete sentence — crawlers see real copy and there is no layout shift when
   * the browser takes over. The animation only ever runs client-side.
   */
  protected readonly typed = signal(this.prompts[0]);

  /**
   * The build the hero console is showing. Fixed sample data, not a live feed —
   * it is a picture of the product, and the `live` rows are what the glow
   * animation attaches to.
   */
  protected readonly agentFeed: AgentEvent[] = [
    { agent: 'Architect', detail: 'Schema — 9 tables, relations locked', state: 'done' },
    { agent: 'UI', detail: 'Booking calendar component', state: 'done' },
    { agent: 'Payments', detail: 'Deploying webhooks…', state: 'live' },
    { agent: 'Auth', detail: 'Sessions + password reset', state: 'live' },
    { agent: 'Deploy', detail: 'Queued — TLS + shareable URL', state: 'next' },
  ];

  protected readonly stats: Stat[] = [
    { value: '< 30 min', label: 'Idea to production URL' },
    { value: 'Built in', label: 'Auth, payments, hosting' },
    { value: 'Web + mobile', label: 'From one description' },
  ];

  protected readonly steps: Step[] = [
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

  protected readonly features: Feature[] = [
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

  protected readonly milestones: Milestone[] = [
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

  protected readonly pricingTiers: PricingTier[] = [
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

  constructor() {
    afterNextRender(() => this.startTypewriter());
  }

  /**
   * Cycles the hero field through `prompts`: hold the finished sentence, delete it,
   * type the next one. Browser-only (see `typed`), and a no-op for anyone who has
   * asked for reduced motion — they keep the seeded sentence, which is already
   * complete and readable.
   */
  private startTypewriter(): void {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      return;
    }

    const HOLD_MS = 2400;
    const TYPE_MS = 38;
    const DELETE_MS = 16;

    let promptIndex = 0;
    let chars = this.prompts[0].length;
    let phase: 'holding' | 'deleting' | 'typing' = 'holding';

    // Advances one frame of the animation and reports how long to wait for the next.
    const advance = (): number => {
      switch (phase) {
        case 'holding':
          phase = 'deleting';
          return DELETE_MS;
        case 'deleting':
          chars -= 1;
          if (chars <= 0) {
            promptIndex = (promptIndex + 1) % this.prompts.length;
            phase = 'typing';
          }
          return DELETE_MS;
        case 'typing':
          chars += 1;
          if (chars >= this.prompts[promptIndex].length) {
            phase = 'holding';
            return HOLD_MS;
          }
          return TYPE_MS;
      }
    };

    const run = () => {
      const delay = advance();
      this.typed.set(this.prompts[promptIndex].slice(0, chars));
      timer = setTimeout(run, delay);
    };

    let timer = setTimeout(run, HOLD_MS);
    this.destroyRef.onDestroy(() => clearTimeout(timer));
  }
}
