import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

/** Presentational pager: it renders the window of page numbers and emits the chosen page. */
@Component({
  selector: 'app-pagination',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pagination.html',
  styleUrl: './pagination.css',
})
export class Pagination {
  /** How many numbered buttons to show around the current page. */
  private static readonly WindowSize = 5;

  readonly page = input.required<number>();
  readonly totalPages = input.required<number>();
  readonly disabled = input(false);

  readonly pageChange = output<number>();

  /** A sliding window, so 167 pages do not render 167 buttons. */
  readonly pages = computed<number[]>(() => {
    const total = this.totalPages();
    const current = this.page();
    if (total <= 0) {
      return [];
    }

    const half = Math.floor(Pagination.WindowSize / 2);
    let start = Math.max(1, current - half);
    const end = Math.min(total, start + Pagination.WindowSize - 1);
    start = Math.max(1, end - Pagination.WindowSize + 1);

    return Array.from({ length: end - start + 1 }, (_, index) => start + index);
  });

  /** Both ends are only reachable when you are not already standing on them. */
  readonly canGoFirst = computed(() => this.page() > 1);
  readonly canGoLast = computed(() => this.page() < this.totalPages());

  goTo(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.page() || this.disabled()) {
      return;
    }
    this.pageChange.emit(page);
  }

  first(): void {
    this.goTo(1);
  }

  last(): void {
    this.goTo(this.totalPages());
  }
}
