import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  OnInit,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Subject, catchError, debounceTime, distinctUntilChanged, switchMap } from 'rxjs';

import { PagedResult, Story } from '../../../core/models/story';
import { Highlight } from '../../../core/pipes/highlight';
import { RelativeTime } from '../../../core/pipes/relative-time';
import { StoryApi } from '../../../core/services/story-api';
import { ThemeStore } from '../../../core/services/theme';
import { Pagination } from '../pagination/pagination';

@Component({
  selector: 'app-story-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Pagination, RelativeTime, Highlight],
  templateUrl: './story-list.html',
  styleUrl: './story-list.css',
})
export class StoryList implements OnInit {
  /** Long enough to avoid a request per keystroke, short enough to feel immediate. */
  static readonly SearchDebounceMs = 300;

  static readonly PageSizes = [10, 20, 30, 50] as const;

  private static readonly HackerNewsItemUrl = 'https://news.ycombinator.com/item?id=';

  private readonly api = inject(StoryApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly themeStore = inject(ThemeStore);

  private readonly searchBox = viewChild<ElementRef<HTMLInputElement>>('searchBox');

  private readonly searchInput$ = new Subject<string>();
  private readonly reload$ = new Subject<void>();

  readonly pageSizes = StoryList.PageSizes;
  readonly theme = this.themeStore.theme;

  /** Raw input value, updated on every keystroke so the clear button can appear immediately. */
  readonly searchText = signal('');
  /** The debounced term actually sent to the API. */
  readonly search = signal('');
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly result = signal<PagedResult<Story> | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  /** Placeholder rows rendered while the first page is still in flight. */
  readonly skeletonRows = Array.from({ length: 8 }, (_, index) => index);

  ngOnInit(): void {
    this.searchInput$
      .pipe(
        debounceTime(StoryList.SearchDebounceMs),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(term => {
        this.search.set(term);
        // A filtered list is a different list, so page 1 is the only sensible position.
        this.page.set(1);
        this.reload$.next();
      });

    this.reload$
      .pipe(
        // switchMap cancels a request that a newer one has superseded, so a slow response
        // for an old page can never overwrite a newer one.
        switchMap(() => {
          this.loading.set(true);
          this.error.set(null);

          return this.api
            .getNewest({ page: this.page(), pageSize: this.pageSize(), search: this.search() })
            .pipe(
              catchError(() => {
                this.error.set('Could not load stories. Please try again.');
                this.loading.set(false);
                return EMPTY;
              }),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(result => {
        this.result.set(result);
        this.loading.set(false);
      });

    this.reload$.next();
  }

  /** "/" focuses search from anywhere, the way most reading apps behave. */
  @HostListener('document:keydown', ['$event'])
  onDocumentKeydown(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    const typing =
      target?.tagName === 'INPUT' ||
      target?.tagName === 'TEXTAREA' ||
      target?.isContentEditable === true;

    if (event.key === '/' && !typing) {
      event.preventDefault();
      this.focusSearch();
      return;
    }

    if (event.key === 'Escape' && target === this.searchBox()?.nativeElement) {
      this.clearSearch();
    }
  }

  onSearchInput(value: string): void {
    this.searchText.set(value);
    this.searchInput$.next(value);
  }

  clearSearch(): void {
    this.searchText.set('');
    this.searchInput$.next('');
    this.focusSearch();
  }

  focusSearch(): void {
    this.searchBox()?.nativeElement.focus();
  }

  onPageSizeChange(value: string): void {
    const size = Number(value);
    if (!StoryList.PageSizes.includes(size as (typeof StoryList.PageSizes)[number])) {
      return;
    }

    this.pageSize.set(size);
    // Row 41 of 20-per-page is not row 41 of 50-per-page, so restart at the top.
    this.page.set(1);
    this.reload$.next();
  }

  onPageChange(page: number): void {
    this.page.set(page);
    this.reload$.next();
    // Jumping to a new page should start at its top, not halfway down the previous one.
    globalThis.scrollTo?.({ top: 0, behavior: 'smooth' });
  }

  toggleTheme(): void {
    this.themeStore.toggle();
  }

  retry(): void {
    this.reload$.next();
  }

  /** Host shown next to the title, e.g. "example.com". Null for text posts. */
  hostOf(story: Story): string | null {
    if (!story.url) {
      return null;
    }

    try {
      return new URL(story.url).hostname.replace(/^www\./, '');
    } catch {
      return null;
    }
  }

  /** Link to the discussion thread on Hacker News itself. */
  discussionUrl(story: Story): string {
    return `${StoryList.HackerNewsItemUrl}${story.id}`;
  }

  /** 1-based position of a story across the whole result set, not just this page. */
  rankOf(index: number, page: PagedResult<Story>): number {
    return (page.page - 1) * page.pageSize + index + 1;
  }
}
