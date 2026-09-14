import { Injectable, effect, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

/**
 * Owns the active colour theme and mirrors it onto the root element as `data-theme`, which is
 * what the stylesheet keys off. Light is the default; the choice is remembered per browser.
 */
@Injectable({ providedIn: 'root' })
export class ThemeStore {
  private static readonly StorageKey = 'hn-reader:theme';

  readonly theme = signal<Theme>(ThemeStore.restore());

  constructor() {
    effect(() => {
      const theme = this.theme();
      document.documentElement.dataset['theme'] = theme;
      ThemeStore.persist(theme);
    });
  }

  toggle(): void {
    this.theme.update(current => (current === 'light' ? 'dark' : 'light'));
  }

  private static restore(): Theme {
    try {
      return localStorage.getItem(ThemeStore.StorageKey) === 'dark' ? 'dark' : 'light';
    } catch {
      // Private browsing or blocked storage: fall back to the default rather than failing.
      return 'light';
    }
  }

  private static persist(theme: Theme): void {
    try {
      localStorage.setItem(ThemeStore.StorageKey, theme);
    } catch {
      // Losing the preference is acceptable; breaking the page is not.
    }
  }
}
