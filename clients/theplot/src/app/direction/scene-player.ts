import { Injectable, signal } from '@angular/core';
import { Cue } from './model/cue';

/** Plays spoken lines in the browser: one cue, or a run of cues in order with their gaps. */
@Injectable({ providedIn: 'root' })
export class ScenePlayer {
  /** The element whose line is playing, or null. */
  readonly playing = signal<string | null>(null);
  /** What started the run that is playing: "scene", a panel's key, or a line's element id. */
  readonly context = signal<string | null>(null);
  /** How far into the playing line, in milliseconds. */
  readonly position = signal(0);
  /** Each audio URL's length once the browser has read it, in milliseconds. */
  readonly durations = signal<ReadonlyMap<string, number>>(new Map());

  private audio: HTMLAudioElement | null = null;
  private run = 0;

  playCue(cue: Cue): Promise<void> {
    return this.playRun([cue], cue.elementId);
  }

  /** Cues in order; a cue's gap is the silence before it, counted from the previous line's end. */
  async playRun(cues: readonly Cue[], context: string): Promise<void> {
    this.stop();
    const run = ++this.run;
    this.context.set(context);
    try {
      for (const [index, cue] of cues.entries()) {
        if (this.run !== run) {
          return;
        }
        if (index > 0 && cue.gapBeforeMs > 0) {
          await new Promise((r) => setTimeout(r, cue.gapBeforeMs));
        }
        if (this.run !== run) {
          return;
        }
        this.playing.set(cue.elementId);
        this.position.set(0);
        await this.play(cue, run);
      }
    } finally {
      if (this.run === run) {
        this.playing.set(null);
        this.context.set(null);
        this.position.set(0);
      }
    }
  }

  stop(): void {
    this.run++;
    this.audio?.pause();
    this.audio = null;
    this.playing.set(null);
    this.context.set(null);
    this.position.set(0);
  }

  private play(cue: Cue, run: number): Promise<void> {
    const url = cue.audio?.url;
    if (!url) {
      return Promise.resolve();
    }
    return new Promise<void>((resolve) => {
      const audio = new Audio(url);
      this.audio = audio;
      const done = () => {
        if (this.audio === audio) {
          this.audio = null;
        }
        resolve();
      };
      audio.addEventListener('loadedmetadata', () => {
        if (Number.isFinite(audio.duration)) {
          this.durations.update((d) => new Map(d).set(url, audio.duration * 1000));
        }
      });
      audio.addEventListener('timeupdate', () => {
        if (this.run === run) {
          this.position.set(audio.currentTime * 1000);
        }
      });
      audio.addEventListener('ended', done, { once: true });
      audio.addEventListener('error', done, { once: true });
      audio.play().catch(done);
      if (this.run !== run) {
        audio.pause();
        done();
      }
    });
  }
}
