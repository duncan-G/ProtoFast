import { computed, inject, Injectable, signal } from '@angular/core';
import { today } from './days';
import { messageOf } from './errors';
import { LoopApi } from './loop-api';
import { Application } from './model/application';
import { Company } from './model/company';
import { Goal } from './model/goal';
import { Note } from './model/note';
import { SentFile } from './model/sent-file';
import { SentFileKind } from './model/sent-file-kind';
import { Todo } from './model/todo';

/** The signed-in user's search, loaded once in the browser and kept in step with each write. */
@Injectable({ providedIn: 'root' })
export class LoopStore {
  private readonly api = inject(LoopApi);
  private loading: Promise<void> | null = null;

  readonly companies = signal<Company[]>([]);
  readonly applications = signal<Application[]>([]);
  readonly notes = signal<Note[]>([]);
  readonly todos = signal<Todo[]>([]);
  readonly goals = signal<Goal[]>([]);
  readonly files = signal<SentFile[]>([]);
  readonly loaded = signal(false);
  readonly error = signal('');
  readonly today = signal(today());

  /** What the notes page lists; company notes are on their company's page. */
  readonly libraryNotes = computed(() => this.notes().filter((note) => !note.companyId));

  readonly companiesById = computed(
    () => new Map(this.companies().map((company) => [company.id, company])),
  );

  readonly applicationsById = computed(
    () => new Map(this.applications().map((application) => [application.id, application])),
  );

  readonly filesById = computed(() => new Map(this.files().map((file) => [file.id, file])));

  /** How many applications each file went with. */
  readonly fileUses = computed(() => {
    const uses = new Map<string, number>();
    for (const { resumeId, cvId } of this.applications()) {
      for (const id of [resumeId, cvId]) {
        if (id) {
          uses.set(id, (uses.get(id) ?? 0) + 1);
        }
      }
    }
    return uses;
  });

  load(): Promise<void> {
    this.loading ??= this.refresh();
    return this.loading;
  }

  async refresh(): Promise<void> {
    this.today.set(today());
    try {
      const [companies, applications, notes, todos, goals, files] = await Promise.all([
        this.api.listCompanies(),
        this.api.listApplications(),
        this.api.listNotes(),
        this.api.listTodos(),
        this.api.listGoals(),
        this.api.listFiles(),
      ]);
      this.companies.set(companies);
      this.applications.set(applications);
      this.notes.set(notes);
      this.todos.set(todos);
      this.goals.set(goals);
      this.files.set(files);
      this.error.set('');
    } catch (err) {
      this.loading = null;
      this.error.set(messageOf(err, 'Your search could not be loaded.'));
    } finally {
      this.loaded.set(true);
    }
  }

  addCompany(name: string, about = ''): Promise<Company> {
    return this.saveCompany({
      id: '',
      name,
      about,
      location: '',
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
      lastModified: 0,
    });
  }

  async saveCompany(company: Company): Promise<Company> {
    const saved = company.id
      ? await this.api.updateCompany(company)
      : await this.api.createCompany(company);
    this.companies.update((list) =>
      [...list.filter((c) => c.id !== saved.id), saved].sort((a, b) =>
        a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }),
      ),
    );
    this.applications.update((list) =>
      list.map((a) => (a.companyId === saved.id ? { ...a, company: saved.name } : a)),
    );
    return saved;
  }

  async deleteCompany(companyId: string): Promise<void> {
    await this.api.deleteCompany(companyId);
    this.companies.update((list) => list.filter((c) => c.id !== companyId));
    this.applications.update((list) => list.filter((a) => a.companyId !== companyId));
    this.notes.update((list) => list.filter((n) => n.companyId !== companyId));
  }

  /** Re-reads the list afterwards, since a change can move it in the loop's order. */
  async saveApplication(application: Application): Promise<Application> {
    const saved = application.id
      ? await this.api.updateApplication(application)
      : await this.api.createApplication(application);
    this.applications.set(await this.api.listApplications());
    return saved;
  }

  addApplication(
    companyId: string,
    fields: Partial<Omit<Application, 'id' | 'companyId'>> = {},
  ): Promise<Application> {
    return this.saveApplication({
      id: '',
      companyId,
      company: '',
      role: '',
      stage: 'applied',
      closed: false,
      appliedOn: today(),
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
    });
  }

  async deleteApplication(applicationId: string): Promise<void> {
    await this.api.deleteApplication(applicationId);
    this.applications.update((list) => list.filter((a) => a.id !== applicationId));
  }

  async saveNote(note: Note): Promise<Note> {
    const saved = note.id ? await this.api.updateNote(note) : await this.api.createNote(note);
    // A note keeps its place while it's open; the next load puts it first.
    this.notes.update((list) =>
      note.id ? list.map((n) => (n.id === saved.id ? saved : n)) : [saved, ...list],
    );
    return saved;
  }

  async importStories(stories: Note[]): Promise<Note[]> {
    const saved = await this.api.importStories(stories);
    this.notes.update((list) => [...saved, ...list]);
    return saved;
  }

  async deleteNote(noteId: string): Promise<void> {
    await this.api.deleteNote(noteId);
    this.notes.update((list) => list.filter((n) => n.id !== noteId));
  }

  async addTodo(label: string, dueOn: string | null): Promise<void> {
    await this.api.createTodo({ id: '', label, dueOn, done: false });
    this.todos.set(await this.api.listTodos());
  }

  async setTodoDone(todo: Todo, done: boolean): Promise<void> {
    const replace = (next: Todo) =>
      this.todos.update((list) => list.map((t) => (t.id === next.id ? next : t)));
    replace({ ...todo, done });
    try {
      replace(await this.api.updateTodo({ ...todo, done }));
    } catch (err) {
      replace(todo);
      throw err;
    }
  }

  async deleteTodo(todoId: string): Promise<void> {
    await this.api.deleteTodo(todoId);
    this.todos.update((list) => list.filter((t) => t.id !== todoId));
  }

  async saveGoal(goal: Goal): Promise<Goal> {
    const saved = goal.id ? await this.api.updateGoal(goal) : await this.api.createGoal(goal);
    this.goals.update((list) =>
      goal.id ? list.map((g) => (g.id === saved.id ? saved : g)) : [...list, saved],
    );
    return saved;
  }

  async deleteGoal(goalId: string): Promise<void> {
    await this.api.deleteGoal(goalId);
    this.goals.update((list) => list.filter((g) => g.id !== goalId));
  }

  async uploadFile(
    kind: SentFileKind,
    file: File,
    name: string,
    onProgress: (fraction: number) => void,
  ): Promise<SentFile> {
    const saved = await this.api.uploadFile(kind, file, name, onProgress);
    this.files.update((list) => [saved, ...list]);
    return saved;
  }

  async renameFile(fileId: string, name: string): Promise<SentFile> {
    const saved = await this.api.renameFile(fileId, name);
    this.files.update((list) => list.map((f) => (f.id === saved.id ? saved : f)));
    return saved;
  }

  openFile(fileId: string): Promise<string> {
    return this.api.openFile(fileId);
  }

  async deleteFile(fileId: string): Promise<void> {
    await this.api.deleteFile(fileId);
    this.files.update((list) => list.filter((f) => f.id !== fileId));
    this.applications.update((list) =>
      list.map((a) => ({
        ...a,
        resumeId: a.resumeId === fileId ? null : a.resumeId,
        cvId: a.cvId === fileId ? null : a.cvId,
      })),
    );
  }
}
