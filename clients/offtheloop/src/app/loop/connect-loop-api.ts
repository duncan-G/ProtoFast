import { inject, Injectable } from '@angular/core';
import type { MessageInitShape } from '@bufbuild/protobuf';
import { createClient } from '@connectrpc/connect';
import {
  type Application as ApplicationMessage,
  Applications,
  Arrangement as ArrangementMessage,
  Companies,
  type Company as CompanyMessage,
  type Document as DocumentMessage,
  DocumentKind as DocumentKindMessage,
  Documents,
  type Goal as GoalMessage,
  Goals,
  type Note as NoteMessage,
  NoteKind as NoteKindMessage,
  Notes,
  type CompanyFindings as CompanyFindingsMessage,
  type PostingFindings as PostingFindingsMessage,
  ResearchAction as ResearchActionMessage,
  type ResearchStep as ResearchStepMessage,
  type Resume as ResumeMessage,
  type ResumeEvidence as ResumeEvidenceMessage,
  type ResumeGroup as ResumeGroupMessage,
  Resumes,
  RoundState as RoundStateMessage,
  type Run as RunMessage,
  RunKind as RunKindMessage,
  type RunResult as RunResultMessage,
  Runs,
  RunStatus as RunStatusMessage,
  type RunWork as RunWorkMessage,
  RunWorkSchema,
  Source as SourceMessage,
  Stage as StageMessage,
  type Todo as TodoMessage,
  Todos,
  type WebSource as WebSourceMessage,
} from '../../lib/gen/OffTheLoop/offtheloop_pb';
import { GRPC_TRANSPORT } from '../grpc-transport';
import { toDay } from './days';
import type { LoopApi } from './loop-api';
import { Application } from './model/application';
import { Arrangement } from './model/arrangement';
import { Company } from './model/company';
import { CompanyFindings } from './model/company-findings';
import { Contact } from './model/contact';
import { Goal } from './model/goal';
import { LoopRun, RunKind, RunState } from './model/loop-run';
import { Note } from './model/note';
import { NoteKind } from './model/note-kind';
import { PostingFindings } from './model/posting-findings';
import { ResearchStep } from './model/research-step';
import { Resume } from './model/resume';
import { ResumeEvidence } from './model/resume-evidence';
import { ResumeGroup } from './model/resume-group';
import { RoundState } from './model/round-state';
import { RunDetails } from './model/run-details';
import { RunResult } from './model/run-result';
import { RunUpdate } from './model/run-update';
import { RunWork } from './model/run-work';
import { SentFile } from './model/sent-file';
import { SentFileKind } from './model/sent-file-kind';
import { Source } from './model/source';
import { Stage } from './model/stage';
import { Todo } from './model/todo';
import { WebSource } from './model/web-source';
import { postToStorage } from './post-to-storage';

const STAGES: Record<Stage, StageMessage> = {
  applied: StageMessage.APPLIED,
  screen: StageMessage.SCREEN,
  tech: StageMessage.TECH,
  onsite: StageMessage.ONSITE,
  offer: StageMessage.OFFER,
};

const ROUND_STATES: Record<RoundState, RoundStateMessage> = {
  upcoming: RoundStateMessage.UPCOMING,
  current: RoundStateMessage.CURRENT,
  done: RoundStateMessage.DONE,
};

const SOURCES: Record<Source, SourceMessage> = {
  referral: SourceMessage.REFERRAL,
  recruiter: SourceMessage.RECRUITER,
  'job board': SourceMessage.JOB_BOARD,
  'careers page': SourceMessage.CAREERS_PAGE,
  other: SourceMessage.OTHER,
};

const ARRANGEMENTS: Record<Arrangement, ArrangementMessage> = {
  remote: ArrangementMessage.REMOTE,
  hybrid: ArrangementMessage.HYBRID,
  onsite: ArrangementMessage.ONSITE,
};

const FILE_KINDS: Record<SentFileKind, DocumentKindMessage> = {
  resume: DocumentKindMessage.RESUME,
  cv: DocumentKindMessage.CV,
};

const NOTE_KINDS: Record<NoteKind, NoteKindMessage> = {
  pattern: NoteKindMessage.PATTERN,
  design: NoteKindMessage.DESIGN,
  company: NoteKindMessage.COMPANY,
  story: NoteKindMessage.STORY,
};

/** The offtheloop package on the api service, over gRPC-Web through the edge. Browser only. */
@Injectable({ providedIn: 'root' })
export class ConnectLoopApi implements LoopApi {
  private readonly transport = inject(GRPC_TRANSPORT);
  private readonly companies = createClient(Companies, this.transport);
  private readonly applications = createClient(Applications, this.transport);
  private readonly notes = createClient(Notes, this.transport);
  private readonly todos = createClient(Todos, this.transport);
  private readonly goals = createClient(Goals, this.transport);
  private readonly documents = createClient(Documents, this.transport);
  private readonly runs = createClient(Runs, this.transport);
  private readonly resumes = createClient(Resumes, this.transport);

  async listCompanies(): Promise<Company[]> {
    const reply = await this.companies.listCompanies({});
    return reply.companies.map(toCompany);
  }

  async createCompany(company: Company): Promise<Company> {
    const reply = await this.companies.createCompany({ company: fromCompany(company) });
    return toCompany(required(reply.company));
  }

  async updateCompany(company: Company): Promise<Company> {
    const reply = await this.companies.updateCompany({ company: fromCompany(company) });
    return toCompany(required(reply.company));
  }

  async deleteCompany(companyId: string): Promise<void> {
    await this.companies.deleteCompany({ companyId });
  }

  async listApplications(): Promise<Application[]> {
    const reply = await this.applications.listApplications({});
    return reply.applications.map(toApplication);
  }

  async createApplication(application: Application): Promise<Application> {
    const reply = await this.applications.createApplication({
      application: fromApplication(application),
    });
    return toApplication(required(reply.application));
  }

  async updateApplication(application: Application): Promise<Application> {
    const reply = await this.applications.updateApplication({
      application: fromApplication(application),
    });
    return toApplication(required(reply.application));
  }

  async deleteApplication(applicationId: string): Promise<void> {
    await this.applications.deleteApplication({ applicationId });
  }

  async listNotes(): Promise<Note[]> {
    const reply = await this.notes.listNotes({});
    return reply.notes.map(toNote);
  }

  async createNote(note: Note): Promise<Note> {
    const reply = await this.notes.createNote({ note: fromNote(note) });
    return toNote(required(reply.note));
  }

  async updateNote(note: Note): Promise<Note> {
    const reply = await this.notes.updateNote({ note: fromNote(note) });
    return toNote(required(reply.note));
  }

  async deleteNote(noteId: string): Promise<void> {
    await this.notes.deleteNote({ noteId });
  }

  async importStories(stories: Note[]): Promise<Note[]> {
    const reply = await this.notes.importStories({ stories: stories.map(fromNote) });
    return reply.stories.map(toNote);
  }

  async startRun(label: string, work: RunWork): Promise<LoopRun> {
    const reply = await this.runs.startRun({ label, work: fromWork(work) });
    return toRun(required(reply.run));
  }

  async listRuns(): Promise<LoopRun[]> {
    const reply = await this.runs.listRuns({});
    return reply.runs.map(toRun);
  }

  async getRun(runId: string): Promise<RunDetails> {
    const reply = await this.runs.getRun({ runId });
    return {
      run: toRun(required(reply.run)),
      work: reply.work ? toWork(reply.work) : null,
      steps: reply.progress?.steps.map(toStep) ?? [],
      text: reply.progress?.text ?? '',
      result: reply.result ? toResult(reply.result) : null,
      noteChanged: reply.noteChanged,
    };
  }

  async *watchRun(
    runId: string,
    from: { attempt: number; steps: number; text: number },
    signal: AbortSignal,
  ): AsyncIterable<RunUpdate> {
    const replies = this.runs.watchRun(
      { runId, attempt: from.attempt, fromStep: from.steps, fromText: from.text },
      { signal },
    );
    for await (const reply of replies) {
      yield {
        run: reply.run ? toRun(reply.run) : null,
        restarted: reply.restarted,
        steps: reply.progress?.steps.map(toStep) ?? [],
        text: reply.progress?.text ?? '',
        result: reply.result ? toResult(reply.result) : null,
      };
    }
  }

  async cancelRun(runId: string): Promise<LoopRun> {
    const reply = await this.runs.cancelRun({ runId });
    return toRun(required(reply.run));
  }

  async dismissRuns(runIds: string[]): Promise<void> {
    await this.runs.dismissRuns({ runIds });
  }

  async listResumes(): Promise<Resume[]> {
    const reply = await this.resumes.listResumes({});
    return reply.resumes.map(toResume);
  }

  async saveResume(resume: Resume): Promise<Resume> {
    const reply = await this.resumes.saveResume({ resume: fromResume(resume) });
    return toResume(required(reply.resume));
  }

  async deleteResume(resumeId: string): Promise<void> {
    await this.resumes.deleteResume({ resumeId });
  }

  async listTodos(): Promise<Todo[]> {
    const reply = await this.todos.listTodos({});
    return reply.todos.map(toTodo);
  }

  async createTodo(todo: Todo): Promise<Todo> {
    const reply = await this.todos.createTodo({ todo: fromTodo(todo) });
    return toTodo(required(reply.todo));
  }

  async updateTodo(todo: Todo): Promise<Todo> {
    const reply = await this.todos.updateTodo({ todo: fromTodo(todo) });
    return toTodo(required(reply.todo));
  }

  async deleteTodo(todoId: string): Promise<void> {
    await this.todos.deleteTodo({ todoId });
  }

  async listGoals(): Promise<Goal[]> {
    const reply = await this.goals.listGoals({});
    return reply.goals.map(toGoal);
  }

  async createGoal(goal: Goal): Promise<Goal> {
    const reply = await this.goals.createGoal({ goal });
    return toGoal(required(reply.goal));
  }

  async updateGoal(goal: Goal): Promise<Goal> {
    const reply = await this.goals.updateGoal({ goal });
    return toGoal(required(reply.goal));
  }

  async deleteGoal(goalId: string): Promise<void> {
    await this.goals.deleteGoal({ goalId });
  }

  async listFiles(): Promise<SentFile[]> {
    const reply = await this.documents.listDocuments({});
    return reply.documents.map(toSentFile);
  }

  async uploadFile(
    kind: SentFileKind,
    file: File,
    name: string,
    onProgress: (fraction: number) => void,
  ): Promise<SentFile> {
    const ticket = await this.documents.createDocumentUpload({
      kind: FILE_KINDS[kind],
      name,
      fileName: file.name,
      sizeBytes: BigInt(file.size),
    });
    await postToStorage(ticket.postUrl, ticket.fields, file, onProgress);
    const reply = await this.documents.completeDocumentUpload({ documentId: ticket.documentId });
    return toSentFile(required(reply.document));
  }

  async renameFile(fileId: string, name: string): Promise<SentFile> {
    const reply = await this.documents.renameDocument({ documentId: fileId, name });
    return toSentFile(required(reply.document));
  }

  async openFile(fileId: string): Promise<string> {
    const reply = await this.documents.openDocument({ documentId: fileId });
    return reply.url;
  }

  async deleteFile(fileId: string): Promise<void> {
    await this.documents.deleteDocument({ documentId: fileId });
  }
}

function required<T>(value: T | undefined): T {
  if (value === undefined) {
    throw new Error('The server sent an empty reply.');
  }
  return value;
}

function keyOf<K extends string, V>(map: Record<K, V>, value: V, fallback: K): K;
function keyOf<K extends string, V>(map: Record<K, V>, value: V, fallback: null): K | null;
function keyOf<K extends string, V>(map: Record<K, V>, value: V, fallback: K | null): K | null {
  return (Object.keys(map) as K[]).find((key) => map[key] === value) ?? fallback;
}

const RESEARCH_ACTIONS: Record<ResearchStep['action'], ResearchActionMessage> = {
  thinking: ResearchActionMessage.THINKING,
  searching: ResearchActionMessage.SEARCHING,
  reading: ResearchActionMessage.READING,
};

const RUN_KINDS: Record<RunKind, RunKindMessage> = {
  researchCompany: RunKindMessage.RESEARCH_COMPANY,
  readPosting: RunKindMessage.READ_POSTING,
  write: RunKindMessage.WRITE,
  readResume: RunKindMessage.READ_RESUME,
  tailorResume: RunKindMessage.TAILOR_RESUME,
};

const RUN_STATES: Record<RunState, RunStatusMessage> = {
  queued: RunStatusMessage.QUEUED,
  running: RunStatusMessage.RUNNING,
  succeeded: RunStatusMessage.SUCCEEDED,
  failed: RunStatusMessage.FAILED,
  cancelled: RunStatusMessage.CANCELLED,
};

function dateOf(unixSeconds: bigint): Date | null {
  return unixSeconds ? new Date(Number(unixSeconds) * 1000) : null;
}

function toRun(message: RunMessage): LoopRun {
  return {
    id: message.id,
    kind: keyOf(RUN_KINDS, message.kind, 'researchCompany'),
    label: message.label,
    state: keyOf(RUN_STATES, message.status, 'queued'),
    error: message.error,
    companyId: message.companyId || null,
    applicationId: message.applicationId || null,
    noteId: message.noteId || null,
    attempt: message.attempt,
    created: dateOf(message.createdUnixSeconds) ?? new Date(),
    started: dateOf(message.startedUnixSeconds),
    finished: dateOf(message.finishedUnixSeconds),
  };
}

function fromWork(work: RunWork): MessageInitShape<typeof RunWorkSchema> {
  switch (work.kind) {
    case 'researchCompany':
      return { work: { case: 'researchCompany', value: { companyId: work.companyId } } };
    case 'readPosting':
      return { work: { case: 'readPosting', value: work.request } };
    case 'write':
      return {
        work: {
          case: 'write',
          value: { ...work.request, kind: NOTE_KINDS[work.request.kind] },
        },
      };
    case 'readResume':
      return { work: { case: 'readResume', value: { documentId: work.documentId } } };
    case 'tailorResume':
      return {
        work: {
          case: 'tailorResume',
          value: { applicationId: work.applicationId, instructions: work.instructions },
        },
      };
  }
}

function toWork(message: RunWorkMessage): RunWork | null {
  const work = message.work;
  switch (work.case) {
    case 'researchCompany':
      return { kind: 'researchCompany', companyId: work.value.companyId };
    case 'readPosting':
      return {
        kind: 'readPosting',
        request: {
          url: work.value.url,
          text: work.value.text,
          companyId: work.value.companyId,
          applicationId: work.value.applicationId,
        },
      };
    case 'write':
      return {
        kind: 'write',
        request: {
          noteId: work.value.noteId,
          instruction: work.value.instruction,
          document: work.value.document,
          selection: work.value.selection,
          title: work.value.title,
          kind: keyOf(NOTE_KINDS, work.value.kind, 'pattern'),
        },
      };
    case 'readResume':
      return { kind: 'readResume', documentId: work.value.documentId };
    case 'tailorResume':
      return {
        kind: 'tailorResume',
        applicationId: work.value.applicationId,
        instructions: work.value.instructions,
      };
    default:
      return null;
  }
}

function toResult(message: RunResultMessage): RunResult | null {
  const result = message.result;
  switch (result.case) {
    case 'company':
      return { kind: 'company', findings: toCompanyFindings(result.value) };
    case 'posting':
      return { kind: 'posting', findings: toPostingFindings(result.value) };
    case 'resume':
      return { kind: 'resume', resume: toResume(required(result.value.resume)) };
    default:
      return null;
  }
}

function toCompanyFindings(message: CompanyFindingsMessage): CompanyFindings {
  const co = toCompany(required(message.company));
  return {
    company: {
      about: co.about,
      location: co.location,
      whatItDoes: co.whatItDoes,
      howItMakesMoney: co.howItMakesMoney,
      revenue: co.revenue,
      growth: co.growth,
      headcount: co.headcount,
      funding: co.funding,
      lastRaised: co.lastRaised,
      values: co.values,
      practices: co.practices,
      competitors: co.competitors,
      concerns: co.concerns,
      contacts: co.contacts,
    },
    sources: message.sources.map(toSource),
  };
}

function toPostingFindings(message: PostingFindingsMessage): PostingFindings {
  const app = toApplication(required(message.application));
  const co = toCompany(required(message.company));
  return {
    application: {
      role: app.role,
      postingUrl: app.postingUrl,
      posting: app.posting,
      postedPay: app.postedPay,
      team: app.team,
      reportsTo: app.reportsTo,
      arrangement: app.arrangement,
      location: app.location,
      hours: app.hours,
      sponsorsVisa: app.sponsorsVisa,
      wants: app.wants,
      stack: app.stack,
      benefits: app.benefits,
      questions: app.questions,
    },
    company: { about: co.about, values: co.values, practices: co.practices },
    employer: message.employer,
    sources: message.sources.map(toSource),
  };
}

function toStep(message: ResearchStepMessage): ResearchStep {
  return { action: keyOf(RESEARCH_ACTIONS, message.action, 'thinking'), subject: message.subject };
}

function toSource(message: WebSourceMessage): WebSource {
  return { title: message.title, url: message.url };
}

function toCompany(message: CompanyMessage): Company {
  return {
    id: message.id,
    name: message.name,
    about: message.about,
    location: message.location,
    whatItDoes: message.whatItDoes,
    howItMakesMoney: message.howItMakesMoney,
    revenue: message.revenue,
    growth: message.growth,
    headcount: message.headcount,
    funding: message.funding,
    lastRaised: message.lastRaised,
    values: [...message.values],
    practices: [...message.practices],
    competitors: [...message.competitors],
    concerns: [...message.concerns],
    contacts: message.contacts.map(
      (contact): Contact => ({
        name: contact.name,
        role: contact.role,
        metOn: contact.metOn ?? null,
        note: contact.note,
      }),
    ),
    starred: message.starred,
    lastModified: Number(message.lastModifiedUnixSeconds),
  };
}

function fromCompany(company: Company) {
  return {
    ...company,
    contacts: company.contacts.map((contact) => ({
      ...contact,
      metOn: contact.metOn ?? undefined,
    })),
  };
}

function toApplication(message: ApplicationMessage): Application {
  return {
    id: message.id,
    companyId: message.companyId,
    company: message.company,
    role: message.role,
    stage: keyOf(STAGES, message.stage, 'applied'),
    closed: message.closed,
    appliedOn: message.appliedOn ?? null,
    source: keyOf(SOURCES, message.source, null),
    sourceDetail: message.sourceDetail,
    resume: message.resume,
    resumeId: message.resumeId || null,
    cvId: message.cvId || null,
    postingUrl: message.postingUrl,
    posting: message.posting,
    nextStep: message.nextStep,
    nextOn: message.nextOn ?? null,
    postedPay: message.postedPay,
    askedPay: message.askedPay,
    offeredPay: message.offeredPay,
    offerTerms: message.offerTerms,
    wants: [...message.wants],
    questions: [...message.questions],
    team: message.team,
    reportsTo: message.reportsTo,
    stack: [...message.stack],
    arrangement: keyOf(ARRANGEMENTS, message.arrangement, null),
    location: message.location,
    hours: message.hours,
    sponsorsVisa: message.sponsorsVisa ?? null,
    benefits: [...message.benefits],
    rounds: message.rounds.map((round) => ({
      label: round.label,
      state: keyOf(ROUND_STATES, round.state, 'upcoming'),
    })),
    learned: message.learned,
    lastModified: Number(message.lastModifiedUnixSeconds),
  };
}

function fromApplication(application: Application) {
  return {
    ...application,
    stage: STAGES[application.stage],
    appliedOn: application.appliedOn ?? undefined,
    source: application.source ? SOURCES[application.source] : SourceMessage.UNSPECIFIED,
    nextOn: application.nextOn ?? undefined,
    arrangement: application.arrangement
      ? ARRANGEMENTS[application.arrangement]
      : ArrangementMessage.UNSPECIFIED,
    sponsorsVisa: application.sponsorsVisa ?? undefined,
    resumeId: application.resumeId ?? '',
    cvId: application.cvId ?? '',
    rounds: application.rounds.map((round) => ({
      label: round.label,
      state: ROUND_STATES[round.state],
    })),
  };
}

function toNote(message: NoteMessage): Note {
  return {
    id: message.id,
    kind: keyOf(NOTE_KINDS, message.kind, 'pattern'),
    companyId: message.companyId || null,
    title: message.title,
    tag: message.tag,
    body: message.body,
    badges: [...message.badges],
    fits: [...message.fits],
  };
}

function fromNote(note: Note) {
  return { ...note, kind: NOTE_KINDS[note.kind], companyId: note.companyId ?? '' };
}

function toResume(message: ResumeMessage): Resume {
  return {
    id: message.id,
    applicationId: message.applicationId || null,
    name: message.name,
    headline: message.headline,
    email: message.email,
    phone: message.phone,
    location: message.location,
    links: message.links.map(({ label, url }) => ({ label, url })),
    summary: message.summary,
    experience: message.experience.map((role) => ({
      company: role.company,
      title: role.title,
      location: role.location,
      start: role.start,
      end: role.end,
      summary: role.summary,
      highlights: [...role.highlights],
      stack: [...role.stack],
      evidence: role.evidence.map(toEvidence),
    })),
    projects: message.projects.map((project) => ({
      name: project.name,
      link: project.link,
      start: project.start,
      end: project.end,
      summary: project.summary,
      highlights: [...project.highlights],
      stack: [...project.stack],
      evidence: project.evidence.map(toEvidence),
    })),
    skills: message.skills.map(toGroup),
    education: message.education.map((school) => ({
      school: school.school,
      degree: school.degree,
      start: school.start,
      end: school.end,
      details: [...school.details],
    })),
    extras: message.extras.map(toGroup),
    notes: [...message.notes],
    lastModified: Number(message.lastModifiedUnixSeconds),
  };
}

function toEvidence({ repo, commit, date, what }: ResumeEvidenceMessage): ResumeEvidence {
  return { repo, commit, date, what };
}

function toGroup(message: ResumeGroupMessage): ResumeGroup {
  return { name: message.name, items: [...message.items] };
}

function fromResume(resume: Resume) {
  const { lastModified: _, ...fields } = resume;
  return { ...fields, applicationId: resume.applicationId ?? '' };
}

function toTodo(message: TodoMessage): Todo {
  return { id: message.id, label: message.label, dueOn: message.dueOn ?? null, done: message.done };
}

function fromTodo(todo: Todo) {
  return { ...todo, dueOn: todo.dueOn ?? undefined };
}

function toSentFile(message: DocumentMessage): SentFile {
  return {
    id: message.id,
    kind: keyOf(FILE_KINDS, message.kind, 'resume'),
    name: message.name,
    fileName: message.fileName,
    mediaType: message.mediaType,
    sizeBytes: Number(message.sizeBytes),
    uploadedOn: toDay(new Date(Number(message.uploadedUnixSeconds) * 1000)),
  };
}

function toGoal(message: GoalMessage): Goal {
  return { id: message.id, kind: message.kind, target: message.target, current: message.current };
}
