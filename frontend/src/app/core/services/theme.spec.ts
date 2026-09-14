import { TestBed } from '@angular/core/testing';

import { ThemeStore } from './theme';

describe('ThemeStore', () => {
  const storageKey = 'hn-reader:theme';

  function create(): ThemeStore {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    const store = TestBed.inject(ThemeStore);
    TestBed.tick();
    return store;
  }

  beforeEach(() => localStorage.removeItem(storageKey));
  afterEach(() => {
    localStorage.removeItem(storageKey);
    delete document.documentElement.dataset['theme'];
  });

  it('defaults to light', () => {
    expect(create().theme()).toBe('light');
  });

  it('mirrors the theme onto the root element', () => {
    const store = create();
    expect(document.documentElement.dataset['theme']).toBe('light');

    store.toggle();
    TestBed.tick();

    expect(document.documentElement.dataset['theme']).toBe('dark');
  });

  it('toggles back and forth', () => {
    const store = create();

    store.toggle();
    expect(store.theme()).toBe('dark');

    store.toggle();
    expect(store.theme()).toBe('light');
  });

  it('remembers the choice for the next visit', () => {
    const store = create();

    store.toggle();
    TestBed.tick();
    expect(localStorage.getItem(storageKey)).toBe('dark');

    expect(create().theme()).toBe('dark');
  });

  it('falls back to light when storage holds something unexpected', () => {
    localStorage.setItem(storageKey, 'chartreuse');

    expect(create().theme()).toBe('light');
  });

  it('still works when storage throws', () => {
    const getItem = spyOn(Storage.prototype, 'getItem').and.throwError('blocked');
    const setItem = spyOn(Storage.prototype, 'setItem').and.throwError('blocked');

    const store = create();
    expect(store.theme()).toBe('light');

    // Losing the preference is acceptable; throwing out of the constructor is not.
    expect(() => {
      store.toggle();
      TestBed.tick();
    }).not.toThrow();

    getItem.and.callThrough();
    setItem.and.callThrough();
  });
});
