import { Injectable } from '@angular/core';
import { ConnectLoopApi } from './connect-loop-api';
import { Application } from './model/application';
import { Company } from './model/company';
import { Goal } from './model/goal';
import { LoopRun } from './model/loop-run';
import { Note } from './model/note';
import { Resume } from './model/resume';
import { RunDetails } from './model/run-details';
import { RunUpdate } from './model/run-update';
import { RunWork } from './model/run-work';
import { SentFile } from './model/sent-file';
import { SentFileKind } from './model/sent-file-kind';
import { Todo } from './model/todo';

/** Ids are ignored on create; another user's ids read as not found. */
@Injectable({ providedIn: 'root', useClass: ConnectLoopApi })
export abstract class LoopApi {
  /** By name. */
  abstract listCompanies(): Promise<Company[]>;

  abstract createCompany(company: Company): Promise<Company>;

  /** Replaces every field, the people included. */
  abstract updateCompany(company: Company): Promise<Company>;

  /** Deletes its applications too, unpinning their notes. */
  abstract deleteCompany(companyId: string): Promise<void>;

  /** Open before closed, the furthest stage first, then the soonest next step. */
  abstract listApplications(): Promise<Application[]>;

  abstract createApplication(application: Application): Promise<Application>;

  /** Replaces every field, the rounds included. */
  abstract updateApplication(application: Application): Promise<Application>;

  /** Unpins its notes. */
  abstract deleteApplication(applicationId: string): Promise<void>;

  /** Most recently modified first. */
  abstract listNotes(): Promise<Note[]>;

  abstract createNote(note: Note): Promise<Note>;

  abstract updateNote(note: Note): Promise<Note>;

  abstract deleteNote(noteId: string): Promise<void>;

  /** Saves every story or, when one is invalid, none; in the order sent. */
  abstract importStories(stories: Note[]): Promise<Note[]>;

  /**
   * Queues AI work: researching a company, reading a posting, writing in a note, reading or
   * tailoring a resume. It runs on the server whether or not anything watches it, and drops earlier
   * runs of the same work on the same thing. Nothing it finds is saved.
   */
  abstract startRun(label: string, work: RunWork): Promise<LoopRun>;

  /** Queued, running and finished runs not yet dismissed, oldest first. */
  abstract listRuns(): Promise<LoopRun[]>;

  abstract getRun(runId: string): Promise<RunDetails>;

  /**
   * The run's progress from what the caller already has, until it ends; stopping the watch leaves
   * it running. Ends early when a proxy cuts the stream, so a caller watches again from where it got.
   */
  abstract watchRun(
    runId: string,
    from: { attempt: number; steps: number; text: number },
    signal: AbortSignal,
  ): AsyncIterable<RunUpdate>;

  /** It stays listed, cancelled. */
  abstract cancelRun(runId: string): Promise<LoopRun>;

  /** Stops any still going. */
  abstract dismissRuns(runIds: string[]): Promise<void>;

  /** The general resume once saved, then the tailored ones, most recently modified first. */
  abstract listResumes(): Promise<Resume[]>;

  /** Replaces the resume for its application, or the general one, making it on the first save. */
  abstract saveResume(resume: Resume): Promise<Resume>;

  abstract deleteResume(resumeId: string): Promise<void>;

  /** The open ones and any done in the last day, soonest due first. */
  abstract listTodos(): Promise<Todo[]>;

  abstract createTodo(todo: Todo): Promise<Todo>;

  abstract updateTodo(todo: Todo): Promise<Todo>;

  abstract deleteTodo(todoId: string): Promise<void>;

  /** Oldest first. */
  abstract listGoals(): Promise<Goal[]>;

  abstract createGoal(goal: Goal): Promise<Goal>;

  abstract updateGoal(goal: Goal): Promise<Goal>;

  abstract deleteGoal(goalId: string): Promise<void>;

  /** Resumes and CVs, newest first. */
  abstract listFiles(): Promise<SentFile[]>;

  /** Sends the file straight to storage, then lists it; an empty name takes the file's. */
  abstract uploadFile(
    kind: SentFileKind,
    file: File,
    name: string,
    onProgress: (fraction: number) => void,
  ): Promise<SentFile>;

  abstract renameFile(fileId: string, name: string): Promise<SentFile>;

  /** A link to the file that works for a few minutes. */
  abstract openFile(fileId: string): Promise<string>;

  /** Takes it off every application it went with. */
  abstract deleteFile(fileId: string): Promise<void>;
}
