import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';
import { PagedResult, Story } from '../models/story';
import { StoryApi } from './story-api';

describe('StoryApi', () => {
  const newestUrl = `${environment.apiBaseUrl}/api/stories/newest`;

  let api: StoryApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    api = TestBed.inject(StoryApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends page and pageSize', () => {
    api.getNewest({ page: 3, pageSize: 25, search: '' }).subscribe();

    const request = http.expectOne(r => r.url === newestUrl);
    expect(request.request.params.get('page')).toBe('3');
    expect(request.request.params.get('pageSize')).toBe('25');
    request.flush(emptyPage());
  });

  it('omits the search parameter when the term is blank', () => {
    api.getNewest({ page: 1, pageSize: 20, search: '   ' }).subscribe();

    const request = http.expectOne(r => r.url === newestUrl);
    expect(request.request.params.has('search')).toBeFalse();
    request.flush(emptyPage());
  });

  it('trims the search term before sending it', () => {
    api.getNewest({ page: 1, pageSize: 20, search: '  rust  ' }).subscribe();

    const request = http.expectOne(r => r.url === newestUrl);
    expect(request.request.params.get('search')).toBe('rust');
    request.flush(emptyPage());
  });

  it('returns the page the API produced', () => {
    let received: PagedResult<Story> | undefined;
    api.getNewest({ page: 1, pageSize: 20, search: '' }).subscribe(r => (received = r));

    const page = { ...emptyPage(), totalCount: 2, items: [story(1), story(2, null)] };
    http.expectOne(r => r.url === newestUrl).flush(page);

    expect(received?.totalCount).toBe(2);
    expect(received?.items[1].url).toBeNull();
  });

  it('surfaces a server error to the caller', () => {
    let status: number | undefined;
    api.getNewest({ page: 1, pageSize: 20, search: '' }).subscribe({
      error: err => (status = err.status),
    });

    http.expectOne(r => r.url === newestUrl).flush('unavailable', {
      status: 503,
      statusText: 'Service Unavailable',
    });

    expect(status).toBe(503);
  });

  it('requests a single story by id', () => {
    api.getById(42).subscribe();

    http.expectOne(`${environment.apiBaseUrl}/api/stories/42`).flush(story(42));
  });

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

  function emptyPage(): PagedResult<Story> {
    return {
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false,
    };
  }
});
