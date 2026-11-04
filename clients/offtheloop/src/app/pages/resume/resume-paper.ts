import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Resume } from '../../loop/model/resume';
import { rangeLabel } from '../../loop/resume-text';

/** The resume as it goes to an employer: on paper when printed, and as its preview. */
@Component({
  selector: 'app-resume-paper',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: block;
    }

    .paper {
      max-width: 816px;
      margin: 0 auto;
      padding: 52px 60px 60px;
      border-radius: 6px;
      background: #fff;
      color: #1d2127;
      font-family: var(--font-sans);
      font-size: 13.5px;
      line-height: 1.45;
      box-shadow: 0 18px 50px rgb(0 0 0 / 0.5);
    }

    h1 {
      margin: 0;
      font-size: 27px;
      font-weight: 650;
      letter-spacing: -0.03em;
      line-height: 1.1;
    }

    .headline {
      margin: 4px 0 0;
      font-size: 15px;
      color: #3b424d;
    }

    .contact {
      margin: 8px 0 0;
      display: flex;
      flex-wrap: wrap;
      gap: 2px 14px;
      font-size: 12.5px;
      color: #4b5361;
    }

    .contact a {
      color: inherit;
      text-decoration: none;
    }

    section {
      margin-top: 20px;
    }

    h2 {
      margin: 0 0 8px;
      padding-bottom: 4px;
      border-bottom: 1px solid #d6dae0;
      font-size: 11.5px;
      font-weight: 600;
      letter-spacing: 0.12em;
      text-transform: uppercase;
      color: #4b5361;
    }

    .summary {
      margin: 14px 0 0;
    }

    .entry + .entry {
      margin-top: 12px;
    }

    .entry-head {
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: 16px;
    }

    .entry-title {
      margin: 0;
      font-size: 14px;
      font-weight: 600;
    }

    .entry-title span {
      font-weight: 400;
      color: #3b424d;
    }

    .when {
      flex: none;
      font-size: 12.5px;
      color: #5a6270;
      font-variant-numeric: tabular-nums;
    }

    .entry-line {
      margin: 1px 0 0;
      color: #4b5361;
      font-size: 12.5px;
    }

    ul {
      margin: 4px 0 0;
      padding-left: 18px;
    }

    li + li {
      margin-top: 2px;
    }

    .groups {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 3px 14px;
    }

    .group-name {
      font-weight: 600;
    }

    @media (max-width: 719px) {
      .paper {
        padding: 28px 22px 32px;
      }
    }

    @media print {
      .paper {
        max-width: none;
        padding: 0;
        border-radius: 0;
        box-shadow: none;
      }

      .entry {
        break-inside: avoid;
      }

      h2 {
        break-after: avoid;
      }
    }
  `,
  template: `
    @let r = resume();
    <article class="paper" [attr.aria-label]="'Resume of ' + (r.name || 'you')">
      <h1>{{ r.name || 'Your name' }}</h1>
      @if (r.headline) {
        <p class="headline">{{ r.headline }}</p>
      }
      @if (contact().length || r.links.length) {
        <p class="contact">
          @for (part of contact(); track $index) {
            <span>{{ part }}</span>
          }
          @for (link of r.links; track link.url) {
            <a [href]="link.url">{{ shortUrl(link.url) }}</a>
          }
        </p>
      }
      @if (r.summary) {
        <p class="summary">{{ r.summary }}</p>
      }

      @if (r.experience.length) {
        <section>
          <h2>Experience</h2>
          @for (role of r.experience; track $index) {
            <div class="entry">
              <div class="entry-head">
                <p class="entry-title">
                  {{ role.title || role.company }}
                  @if (role.title) {
                    <span>· {{ role.company }}</span>
                  }
                </p>
                <span class="when">{{ range(role.start, role.end, 'Present') }}</span>
              </div>
              @if (role.location || role.summary) {
                <p class="entry-line">
                  {{ [role.location, role.summary].filter(present).join(' · ') }}
                </p>
              }
              @if (role.highlights.length) {
                <ul>
                  @for (line of role.highlights; track $index) {
                    <li>{{ line }}</li>
                  }
                </ul>
              }
            </div>
          }
        </section>
      }

      @if (r.projects.length) {
        <section>
          <h2>Projects</h2>
          @for (project of r.projects; track $index) {
            <div class="entry">
              <div class="entry-head">
                <p class="entry-title">
                  {{ project.name }}
                  @if (project.link) {
                    <span>· {{ shortUrl(project.link) }}</span>
                  }
                </p>
                <span class="when">{{ range(project.start, project.end, 'Present') }}</span>
              </div>
              @if (project.summary || project.stack.length) {
                <p class="entry-line">
                  {{ [project.summary, project.stack.join(', ')].filter(present).join(' · ') }}
                </p>
              }
              @if (project.highlights.length) {
                <ul>
                  @for (line of project.highlights; track $index) {
                    <li>{{ line }}</li>
                  }
                </ul>
              }
            </div>
          }
        </section>
      }

      @if (r.skills.length) {
        <section>
          <h2>Skills</h2>
          <div class="groups">
            @for (group of r.skills; track $index) {
              <span class="group-name">{{ group.name }}</span>
              <span>{{ group.items.join(', ') }}</span>
            }
          </div>
        </section>
      }

      @if (r.education.length) {
        <section>
          <h2>Education</h2>
          @for (school of r.education; track $index) {
            <div class="entry">
              <div class="entry-head">
                <p class="entry-title">
                  {{ school.degree || school.school }}
                  @if (school.degree) {
                    <span>· {{ school.school }}</span>
                  }
                </p>
                <span class="when">{{ range(school.start, school.end, null) }}</span>
              </div>
              @if (school.details.length) {
                <ul>
                  @for (line of school.details; track $index) {
                    <li>{{ line }}</li>
                  }
                </ul>
              }
            </div>
          }
        </section>
      }

      @for (group of r.extras; track $index) {
        <section>
          <h2>{{ group.name }}</h2>
          <ul>
            @for (line of group.items; track $index) {
              <li>{{ line }}</li>
            }
          </ul>
        </section>
      }
    </article>
  `,
})
export class ResumePaper {
  readonly resume = input.required<Resume>();

  protected readonly range = rangeLabel;
  protected readonly present = Boolean;

  protected readonly contact = computed(() => {
    const r = this.resume();
    return [r.location, r.email, r.phone].filter(Boolean);
  });

  protected shortUrl(url: string): string {
    return url.replace(/^https?:\/\/(www\.)?/, '').replace(/\/$/, '');
  }
}
