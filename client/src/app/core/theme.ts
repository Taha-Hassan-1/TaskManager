import { Injectable, signal } from '@angular/core';

export type ThemeChoice = 'system' | 'light' | 'dark';

/** The same key and values public/theme-init.js reads before the page paints. */
const KEY = 'teamboard-theme';
const ORDER: ThemeChoice[] = ['system', 'light', 'dark'];

/**
 * Light, dark, or whatever the operating system prefers. The choice is a `data-theme` attribute on <html>
 * (none means "follow the system"), which styles.scss turns into colours.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly choice = signal<ThemeChoice>(this.read());

  constructor() {
    this.apply(this.choice());
  }

  /** The choice after this one, in the order system, light, dark. */
  next(): ThemeChoice {
    return ORDER[(ORDER.indexOf(this.choice()) + 1) % ORDER.length];
  }

  cycle(): void {
    this.set(this.next());
  }

  set(choice: ThemeChoice): void {
    this.choice.set(choice);
    this.apply(choice);
    try {
      if (choice === 'system') localStorage.removeItem(KEY);
      else localStorage.setItem(KEY, choice);
    } catch {
      /* storage can be blocked (private windows): the choice then lasts until the page closes */
    }
  }

  private read(): ThemeChoice {
    try {
      const stored = localStorage.getItem(KEY);
      return stored === 'light' || stored === 'dark' ? stored : 'system';
    } catch {
      return 'system';
    }
  }

  private apply(choice: ThemeChoice): void {
    const root = document.documentElement;
    if (choice === 'system') root.removeAttribute('data-theme');
    else root.setAttribute('data-theme', choice);
  }
}
