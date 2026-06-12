import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ImagePlaceholder } from './image-placeholder';

interface Step {
  index: string;
  title: string;
  description: string;
}

interface Feature {
  iconPath: string;
  title: string;
  description: string;
}

interface TimelineEntry {
  time: string;
  title: string;
  description: string;
}

interface PricingTier {
  name: string;
  price: string;
  period: string;
  blurb: string;
  features: string[];
  cta: string;
  highlighted: boolean;
}

@Component({
  selector: 'app-landing',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ImagePlaceholder],
  templateUrl: './landing.html',
})
export class Landing {
  protected readonly steps: Step[] = [
    {
      index: '01',
      title: 'Describe it',
      description:
        'Type what you want to build the way you would explain it to a developer friend. No specs, no wireframes, no tickets — plain English is the whole interface.',
    },
    {
      index: '02',
      title: 'Agents prototype it',
      description:
        'A coordinated set of agents splits the work — data model, API, UI, auth, payments — and builds in parallel while you watch every decision land in real time.',
    },
    {
      index: '03',
      title: 'It is live',
      description:
        'Your prototype deploys to production hosting with a shareable URL, sign-in that works, and payments that can charge real cards. Under 30 minutes, start to finish.',
    },
  ];

  protected readonly features: Feature[] = [
    {
      iconPath: 'm3.75 13.5 10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75Z',
      title: 'An agent swarm, not a queue',
      description:
        'Specialized agents for architecture, UI, API, and infrastructure build concurrently — the reason a working app takes minutes instead of months.',
    },
    {
      iconPath:
        'M16.5 10.5V6.75a4.5 4.5 0 1 0-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 0 0 2.25-2.25v-6.75a2.25 2.25 0 0 0-2.25-2.25H6.75a2.25 2.25 0 0 0-2.25 2.25v6.75a2.25 2.25 0 0 0 2.25 2.25Z',
      title: 'Auth out of the box',
      description:
        'Sign-up, sign-in, sessions, and password reset are wired into every prototype from the first build. Your testers log in like it is a real product — because it is.',
    },
    {
      iconPath:
        'M2.25 8.25h19.5M2.25 9h19.5m-16.5 5.25h6m-6 2.25h3m-3.75 3h15a2.25 2.25 0 0 0 2.25-2.25V6.75A2.25 2.25 0 0 0 19.5 4.5h-15a2.25 2.25 0 0 0-2.25 2.25v10.5A2.25 2.25 0 0 0 4.5 19.5Z',
      title: 'Payments built in',
      description:
        'Subscriptions, one-time checkout, and customer billing come pre-integrated. Flip the switch from test mode and your prototype starts taking real money.',
    },
    {
      iconPath:
        'M10.5 1.5H8.25A2.25 2.25 0 0 0 6 3.75v16.5a2.25 2.25 0 0 0 2.25 2.25h7.5A2.25 2.25 0 0 0 18 20.25V3.75a2.25 2.25 0 0 0-2.25-2.25H13.5m-3 0V3h3V1.5m-3 0h3m-3 18.75h3',
      title: 'Web and mobile from one prompt',
      description:
        'Every build ships a responsive web app and a mobile-ready experience together. One description, every screen size your users actually have.',
    },
    {
      iconPath:
        'M12 21a9.004 9.004 0 0 0 8.716-6.747M12 21a9.004 9.004 0 0 1-8.716-6.747M12 21c2.485 0 4.5-4.03 4.5-9S14.485 3 12 3m0 18c-2.485 0-4.5-4.03-4.5-9S9.515 3 12 3m0 0a8.997 8.997 0 0 1 7.843 4.582M12 3a8.997 8.997 0 0 0-7.843 4.582m15.686 0A11.953 11.953 0 0 1 12 10.5c-2.998 0-5.74-1.1-7.843-2.918m15.686 0A8.959 8.959 0 0 1 21 12c0 .778-.099 1.533-.284 2.253m0 0A17.919 17.919 0 0 1 12 16.5c-3.162 0-6.133-.815-8.716-2.247m0 0A9.015 9.015 0 0 1 3 12c0-1.605.42-3.113 1.157-4.418',
      title: 'Production from minute one',
      description:
        'No localhost, no staging purgatory. Builds deploy to managed hosting with TLS and a shareable URL the moment the agents finish.',
    },
    {
      iconPath:
        'M20.25 8.511c.884.284 1.5 1.128 1.5 2.097v4.286c0 1.136-.847 2.1-1.98 2.193-.34.027-.68.052-1.02.072v3.091l-3-3c-1.354 0-2.694-.055-4.02-.163a2.115 2.115 0 0 1-.825-.242m9.345-8.334a2.126 2.126 0 0 0-.476-.095 48.64 48.64 0 0 0-8.048 0c-1.131.094-1.976 1.057-1.976 2.192v4.286c0 .837.46 1.58 1.155 1.951m9.345-8.334V6.637c0-1.621-1.152-3.026-2.76-3.235A48.455 48.455 0 0 0 11.25 3c-2.115 0-4.198.137-6.24.402-1.608.209-2.76 1.614-2.76 3.235v6.226c0 1.621 1.152 3.026 2.76 3.235.577.075 1.157.14 1.74.194V21l4.155-4.155',
      title: 'Iterate by chatting',
      description:
        '"Make the dashboard dark." "Add a referral program." Changes ship to the live prototype in minutes — feedback loops measured in coffee sips, not sprints.',
    },
  ];

  protected readonly timeline: TimelineEntry[] = [
    {
      time: '0:00',
      title: 'You hit "Prototype it"',
      description: 'Your description lands and the planning agent drafts the architecture.',
    },
    {
      time: '0:02',
      title: 'The swarm fans out',
      description: 'UI, API, data, auth, and payments agents start building in parallel.',
    },
    {
      time: '0:12',
      title: 'First working preview',
      description: 'Click through real screens while the agents keep wiring up the backend.',
    },
    {
      time: '0:22',
      title: 'Auth and payments go green',
      description: 'Test accounts sign in, test cards charge, webhooks fire.',
    },
    {
      time: '0:28',
      title: 'Deployed to production',
      description: 'A shareable URL with TLS, ready for your first real users.',
    },
  ];

  protected readonly pricingTiers: PricingTier[] = [
    {
      name: 'Starter',
      price: '$0',
      period: 'forever',
      blurb: 'For trying the magic trick yourself.',
      features: [
        '3 prototypes per month',
        'Web + mobile output',
        'Auth included',
        'Test-mode payments',
        'protofast.app subdomain',
      ],
      cta: 'Start free',
      highlighted: false,
    },
    {
      name: 'Pro',
      price: '$49',
      period: 'per month',
      blurb: 'For founders shipping for real.',
      features: [
        'Unlimited prototypes',
        'Live payments — keep 100% of revenue',
        'Custom domains',
        'Iterate-by-chat on deployed apps',
        'Export the full source code',
      ],
      cta: 'Go Pro',
      highlighted: true,
    },
    {
      name: 'Team',
      price: '$199',
      period: 'per month',
      blurb: 'For agencies and product teams.',
      features: [
        'Everything in Pro',
        '5 seats, shared workspaces',
        'White-label client previews',
        'Priority agent capacity',
        'SSO and audit logs',
      ],
      cta: 'Start a team',
      highlighted: false,
    },
  ];
}
