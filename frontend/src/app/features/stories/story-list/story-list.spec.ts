import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { environment } from '../../../../environments/environment';
import { PagedResult, Story } from '../../../core/models/story';
import { StoryList } from './story-list';

describe('StoryList', () => {
  const newestUrl = `${environment.apiBaseUrl}/api/stories/newest`;

  let fixture: ComponentFixture<StoryList>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StoryList],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(StoryList);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the first page on init', () => {
    fixture.detectChanges();

    const request = http.expectOne(r => r.url === newestUrl);
    expect(request.request.params.get('page')).toBe('1');
    request.flush(page([story(1), story(2)], { totalCount: 2, totalPages: 1 }));
    fixture.detectChanges();

    expect(titles()).toEqual(['Story 1', 'Story 2']);
  });

  it('renders a linked story as an anchor', () => {
    fixture.detectChanges();
    flushInitial([story(1, 'https://example.com/article')]);

    const anchor = fixture.debugElement.query(By.css('.story__title'));
    expect(anchor.nativeElement.tagName).toBe('A');
    expect(anchor.nativeElement.getAttribute('href')).toBe('https://example.com/article');
    expect(anchor.nativeElement.getAttribute('rel')).toContain('noopener');
  });

  it('renders a story with no url as plain text rather than a dead link', () => {
    fixture.detectChanges();
    flushInitial([story(1, null)]);

    expect(fixture.debugElement.query(By.css('a.story__title'))).toBeNull();

    const plain = fixture.debugElement.query(By.css('.story__title--plain'));
    expect(plain.nativeElement.tagName).toBe('SPAN');
    expect(plain.nativeElement.textContent.trim()).toBe('Story 1');
    expect(fixture.debugElement.query(By.css('.story__badge'))).not.toBeNull();
  });

  it('omits the author line for a story that has none', () => {
    fixture.detectChanges();
    flushInitial([{ ...story(1), by: null }]);

    const meta = fixture.debugElement.query(By.css('.story__meta')).nativeElement.textContent;

    // No placeholder text where the author would have been.
    expect(meta).not.toContain('null');
    expect(meta).not.toContain('undefined');
    expect(meta).not.toContain('author');
    // The rest of the meta row must survive the missing author.
    expect(meta).toContain('comments');
    expect(fixture.debugElement.query(By.css('.score')).nativeElement.textContent).toContain('1');
  });

  it('still shows the author when there is one', () => {
    // Control for the test above: the absence must be caused by the null, not by a broken row.
    fixture.detectChanges();
    flushInitial([{ ...story(1), by: 'patio11' }]);

    expect(fixture.debugElement.query(By.css('.story__meta')).nativeElement.textContent)
      .toContain('patio11');
  });

  it('renders a linkless story with no author without producing a link', () => {
    // Both optional fields absent at once — the combination the template branches twice on.
    fixture.detectChanges();
    flushInitial([{ ...story(1), url: null, by: null }]);

    expect(fixture.debugElement.query(By.css('a.story__title'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.story__badge')).nativeElement.textContent.trim())
      .toBe('no link');
    expect(fixture.debugElement.query(By.css('.story__title--plain')).nativeElement.textContent)
      .toBe('Story 1');
  });

  it('says "1 comment" for one and "comments" for none', () => {
    fixture.detectChanges();
    flushInitial([
      { ...story(1), descendants: 1 },
      { ...story(2), descendants: 0 },
      { ...story(3), descendants: 42 },
    ]);

    const links = fixture.debugElement
      .queryAll(By.css('.meta--link'))
      .map(el => el.nativeElement.textContent.trim());

    expect(links).toEqual(['1 comment', '0 comments', '42 comments']);
  });

  it('says "1 story" in the summary when exactly one matches', fakeAsync(() => {
    fixture.detectChanges();
    flushInitial([story(1)]);

    type('rust');
    tick(StoryList.SearchDebounceMs);
    http.expectOne(r => r.url === newestUrl).flush(page([story(2)], { totalCount: 1 }));
    fixture.detectChanges();

    const summary = fixture.debugElement.query(By.css('.summary')).nativeElement.textContent;
    expect(summary).toContain('1 story');
    expect(summary).not.toContain('1 stories');
  }));

  it('hides the pager when everything fits on one page', () => {
    fixture.detectChanges();
    flushInitial([story(1), story(2)], { totalCount: 2, totalPages: 1 });

    expect(fixture.debugElement.query(By.css('.pager'))).toBeNull();
  });

  it('debounces typing into a single request', fakeAsync(() => {
    fixture.detectChanges();
    flushInitial([story(1)]);

    type('r');
    type('ru');
    type('rust');
    tick(StoryList.SearchDebounceMs - 1);
    http.expectNone(r => r.url === newestUrl);

    tick(1);
    const request = http.expectOne(r => r.url === newestUrl);
    expect(request.request.params.get('search')).toBe('rust');
    request.flush(page([story(3)], { totalCount: 1, totalPages: 1 }));
    fixture.detectChanges();

    expect(titles()).toEqual(['Story 3']);
  }));

  it('returns to page 1 when the search changes', fakeAsync(() => {
    fixture.detectChanges();
    flushInitial([story(1)], { totalCount: 100, totalPages: 5 });

    fixture.componentInstance.onPageChange(3);
    http.expectOne(r => r.params.get('page') === '3')
      .flush(page([story(2)], { totalCount: 100, totalPages: 5, page: 3 }));

    type('rust');
    tick(StoryList.SearchDebounceMs);

    const request = http.expectOne(r => r.url === newestUrl);
    expect(request.request.params.get('page')).toBe('1');
    request.flush(page([], { totalCount: 0, totalPages: 0 }));
  }));

  it('requests the page the pager asks for', () => {
    fixture.detectChanges();
    flushInitial([story(1)], { totalCount: 100, totalPages: 5 });

    fixture.componentInstance.onPageChange(4);

    const request = http.expectOne(r => r.url === newestUrl);
    expect(request.request.params.get('page')).toBe('4');
    request.flush(page([story(9)], { totalCount: 100, totalPages: 5, page: 4 }));
  });

  it('shows an empty-result message naming the search term', () => {
    fixture.detectChanges();
    flushInitial([story(1)]);

    fixture.componentInstance.onPageChange(1);
    http.expectOne(r => r.url === newestUrl).flush(page([], { totalCount: 0, totalPages: 0 }));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No stories to show.');
  });

  it('shows an error with a retry that re-requests', () => {
    fixture.detectChanges();
    http.expectOne(r => r.url === newestUrl).flush('down', {
      status: 503,
      statusText: 'Service Unavailable',
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Could not load stories.');
    expect(fixture.componentInstance.loading()).toBeFalse();

    fixture.debugElement.query(By.css('.notice__action')).nativeElement.click();
    http.expectOne(r => r.url === newestUrl).flush(page([story(1)], { totalCount: 1, totalPages: 1 }));
    fixture.detectChanges();

    expect(fixture.componentInstance.error()).toBeNull();
    expect(titles()).toEqual(['Story 1']);
  });

  it('highlights the search term inside matching titles', fakeAsync(() => {
    fixture.detectChanges();
    flushInitial([story(1)]);

    type('rust');
    tick(StoryList.SearchDebounceMs);
    http.expectOne(r => r.url === newestUrl).flush(
      page([{ ...story(1), title: 'Learning Rust today' }], { totalCount: 1 }),
    );
    fixture.detectChanges();

    const marks = fixture.debugElement
      .queryAll(By.css('.story__title .story__match'))
      .map(el => el.nativeElement.textContent);

    expect(marks).toEqual(['Rust']);
  }));

  it('leaves the rendered title identical to the original', fakeAsync(() => {
    // Every segment renders as its own element, so Angular drops the whitespace between them.
    // If that ever changed, titles would gain stray spaces around each match.
    fixture.detectChanges();
    flushInitial([story(1)]);

    type('rust');
    tick(StoryList.SearchDebounceMs);
    http.expectOne(r => r.url === newestUrl).flush(
      page([{ ...story(1), title: 'Learning Rust today' }], { totalCount: 1 }),
    );
    fixture.detectChanges();

    const rendered = fixture.debugElement.query(By.css('.story__title')).nativeElement.textContent;
    expect(rendered).toBe('Learning Rust today');
  }));

  it('highlights a matching author too', fakeAsync(() => {
    fixture.detectChanges();
    flushInitial([story(1)]);

    type('author');
    tick(StoryList.SearchDebounceMs);
    http.expectOne(r => r.url === newestUrl).flush(
      page([{ ...story(1), by: 'author42' }], { totalCount: 1 }),
    );
    fixture.detectChanges();

    const marks = fixture.debugElement
      .queryAll(By.css('.story__meta .story__match'))
      .map(el => el.nativeElement.textContent);

    expect(marks).toEqual(['author']);
  }));

  it('highlights nothing when there is no search term', () => {
    fixture.detectChanges();
    flushInitial([{ ...story(1), title: 'Learning Rust today' }]);

    expect(fixture.debugElement.queryAll(By.css('.story__match')).length).toBe(0);
    expect(fixture.debugElement.query(By.css('.story__title')).nativeElement.textContent)
      .toBe('Learning Rust today');
  });

  it('highlights a linkless story without turning it into a link', fakeAsync(() => {
    fixture.detectChanges();
    flushInitial([story(1)]);

    type('ask');
    tick(StoryList.SearchDebounceMs);
    http.expectOne(r => r.url === newestUrl).flush(
      page([{ ...story(1), title: 'Ask HN: anything', url: null }], { totalCount: 1 }),
    );
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('a.story__title'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.story__title--plain .story__match'))
      .nativeElement.textContent).toBe('Ask');
  }));

  it('renders a hostile title as text even while highlighting it', fakeAsync(() => {
    // The highlight path must not become a way back to innerHTML.
    fixture.detectChanges();
    flushInitial([story(1)]);

    type('img');
    tick(StoryList.SearchDebounceMs);
    http.expectOne(r => r.url === newestUrl).flush(
      page([{ ...story(1), title: '<img src=x onerror="alert(1)">' }], { totalCount: 1 }),
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.story__title img')).toBeNull();
    expect(fixture.debugElement.query(By.css('.story__title')).nativeElement.textContent)
      .toBe('<img src=x onerror="alert(1)">');
  }));

  // Story titles and urls are submitted by Hacker News users and rendered verbatim, so these
  // pin the escaping we rely on rather than trusting it by reputation.
  it('neutralises a javascript: url instead of rendering it as a working link', () => {
    fixture.detectChanges();
    flushInitial([{ ...story(1), url: 'javascript:alert(document.domain)' }]);

    const href = fixture.debugElement
      .query(By.css('a.story__title'))
      .nativeElement.getAttribute('href');

    expect(href.startsWith('javascript:')).toBeFalse();
    expect(href).toContain('unsafe:');
  });

  it('escapes HTML in a story title rather than executing it', () => {
    fixture.detectChanges();
    flushInitial([{ ...story(1), title: '<img src=x onerror="alert(1)">' }]);

    // Rendered as text, not parsed into an element.
    expect(fixture.nativeElement.querySelector('.story__title img')).toBeNull();
    expect(fixture.debugElement.query(By.css('.story__title')).nativeElement.textContent)
      .toContain('<img src=x');
  });

  it('does not treat a hostile url as a host label', () => {
    const component = fixture.componentInstance;

    expect(component.hostOf({ ...story(1), url: 'javascript:alert(1)' })).toBe('');
  });

  it('extracts the host for display and drops the www prefix', () => {
    const component = fixture.componentInstance;

    expect(component.hostOf(story(1, 'https://www.example.com/a'))).toBe('example.com');
    expect(component.hostOf(story(1, 'not a url'))).toBeNull();
    expect(component.hostOf(story(1, null))).toBeNull();
  });

  it('clears the search and reloads unfiltered', fakeAsync(() => {
    fixture.detectChanges();
    flushInitial([story(1)]);

    type('rust');
    tick(StoryList.SearchDebounceMs);
    http.expectOne(r => r.url === newestUrl).flush(page([story(2)], { totalCount: 1 }));
    fixture.detectChanges();

    fixture.debugElement.query(By.css('.search__clear')).nativeElement.click();
    tick(StoryList.SearchDebounceMs);

    const request = http.expectOne(r => r.url === newestUrl);
    expect(request.request.params.has('search')).toBeFalse();
    expect(fixture.componentInstance.searchText()).toBe('');
    request.flush(page([story(1)]));
  }));

  it('requests the chosen page size and returns to page 1', () => {
    fixture.detectChanges();
    flushInitial([story(1)], { totalCount: 500, totalPages: 25 });

    fixture.componentInstance.onPageChange(3);
    http.expectOne(r => r.params.get('page') === '3')
      .flush(page([story(2)], { totalCount: 500, totalPages: 25, page: 3 }));

    fixture.componentInstance.onPageSizeChange('50');

    const request = http.expectOne(r => r.url === newestUrl);
    expect(request.request.params.get('pageSize')).toBe('50');
    // Row 41 of 20-per-page is not row 41 of 50-per-page, so the position must reset.
    expect(request.request.params.get('page')).toBe('1');
    request.flush(page([story(3)], { totalCount: 500, totalPages: 10 }));
  });

  it('ignores a page size that is not on the menu', () => {
    fixture.detectChanges();
    flushInitial([story(1)]);

    fixture.componentInstance.onPageSizeChange('999');

    http.expectNone(r => r.url === newestUrl);
    expect(fixture.componentInstance.pageSize()).toBe(20);
  });

  it('clears the search when Escape is pressed in the box', fakeAsync(() => {
    fixture.detectChanges();
    flushInitial([story(1)]);

    type('rust');
    tick(StoryList.SearchDebounceMs);
    http.expectOne(r => r.url === newestUrl).flush(page([story(2)], { totalCount: 1 }));
    fixture.detectChanges();

    const input: HTMLInputElement =
      fixture.debugElement.query(By.css('.search__input')).nativeElement;
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    tick(StoryList.SearchDebounceMs);

    expect(fixture.componentInstance.searchText()).toBe('');
    const request = http.expectOne(r => r.url === newestUrl);
    expect(request.request.params.has('search')).toBeFalse();
    request.flush(page([story(1)]));
  }));

  it('ignores Escape pressed outside the search box', fakeAsync(() => {
    fixture.detectChanges();
    flushInitial([story(1)]);

    type('rust');
    tick(StoryList.SearchDebounceMs);
    http.expectOne(r => r.url === newestUrl).flush(page([story(2)], { totalCount: 1 }));

    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    tick(StoryList.SearchDebounceMs);

    expect(fixture.componentInstance.searchText()).toBe('rust');
    http.expectNone(r => r.url === newestUrl);
  }));

  it('toggles the theme', () => {
    fixture.detectChanges();
    flushInitial([story(1)]);

    const before = fixture.componentInstance.theme();
    fixture.componentInstance.toggleTheme();

    expect(fixture.componentInstance.theme()).not.toBe(before);
  });

  it('builds a Hacker News discussion link and a whole-feed rank', () => {
    const component = fixture.componentInstance;

    expect(component.discussionUrl(story(4242))).toBe(
      'https://news.ycombinator.com/item?id=4242',
    );
    // Third row of page 3 at 20 per page is item 43 overall.
    expect(component.rankOf(2, page([], { page: 3, pageSize: 20 }))).toBe(43);
  });

  it('focuses the search box when / is pressed', () => {
    fixture.detectChanges();
    flushInitial([story(1)]);

    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: '/', bubbles: true }));
    fixture.detectChanges();

    const input = fixture.debugElement.query(By.css('.search__input')).nativeElement;
    expect(document.activeElement).toBe(input);
  });

  it('does not hijack / while the user is already typing', () => {
    fixture.detectChanges();
    flushInitial([story(1)]);

    const input: HTMLInputElement =
      fixture.debugElement.query(By.css('.search__input')).nativeElement;
    const event = new KeyboardEvent('keydown', { key: '/', bubbles: true, cancelable: true });
    input.dispatchEvent(event);

    expect(event.defaultPrevented).toBeFalse();
  });

  function type(value: string): void {
    const input = fixture.debugElement.query(By.css('.search__input')).nativeElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function titles(): string[] {
    return fixture.debugElement
      .queryAll(By.css('.story__title'))
      .map(el => el.nativeElement.textContent.trim());
  }

  function flushInitial(items: Story[], overrides: Partial<PagedResult<Story>> = {}): void {
    http.expectOne(r => r.url === newestUrl).flush(
      page(items, { totalCount: items.length, totalPages: 1, ...overrides }),
    );
    fixture.detectChanges();
  }

  function story(id: number, url: string | null = `https://example.com/${id}`): Story {
    return {
      id,
      title: `Story ${id}`,
      url,
      by: `author${id}`,
      time: '2026-01-01T00:00:00+00:00',
      score: id,
      descendants: 0,
    };
  }

  function page(items: Story[], overrides: Partial<PagedResult<Story>> = {}): PagedResult<Story> {
    const base: PagedResult<Story> = {
      items,
      totalCount: items.length,
      page: 1,
      pageSize: 20,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    };
    return { ...base, ...overrides };
  }
});
