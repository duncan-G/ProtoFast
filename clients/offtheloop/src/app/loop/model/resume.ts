import { ResumeGroup } from './resume-group';
import { ResumeLink } from './resume-link';
import { ResumeProject } from './resume-project';
import { ResumeRole } from './resume-role';
import { ResumeSchool } from './resume-school';

/**
 * The general resume holds everything that could go on a resume; one tailored to an application is
 * what goes to that employer.
 */
export interface Resume {
  id: string;
  /** Null for the general resume. */
  applicationId: string | null;
  name: string;
  /** One line under the name: "Senior backend engineer · payments". */
  headline: string;
  email: string;
  phone: string;
  location: string;
  links: ResumeLink[];
  summary: string;
  experience: ResumeRole[];
  projects: ResumeProject[];
  skills: ResumeGroup[];
  education: ResumeSchool[];
  /** Certifications, awards, talks, open source and the like. */
  extras: ResumeGroup[];
  /** A tailored resume's: what it leans on, and what the job asks that it can't show. */
  notes: string[];
  /** Unix seconds. */
  lastModified: number;
}

export function blankResume(applicationId: string | null = null): Resume {
  return {
    id: '',
    applicationId,
    name: '',
    headline: '',
    email: '',
    phone: '',
    location: '',
    links: [],
    summary: '',
    experience: [],
    projects: [],
    skills: [],
    education: [],
    extras: [],
    notes: [],
    lastModified: 0,
  };
}

/** Nothing on it to tailor, as the server counts it. */
export function isEmptyResume(resume: Resume | null): boolean {
  return (
    !resume ||
    (!resume.summary &&
      !resume.experience.length &&
      !resume.projects.length &&
      !resume.skills.length &&
      !resume.education.length &&
      !resume.extras.length)
  );
}
