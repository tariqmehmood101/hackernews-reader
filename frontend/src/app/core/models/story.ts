/** A Hacker News story as returned by the API. */
export interface Story {
  readonly id: number;
  readonly title: string;
  /** Null for text posts such as "Ask HN", which have no article to link to. */
  readonly url: string | null;
  readonly by: string | null;
  readonly time: string;
  readonly score: number;
  readonly descendants: number;
}

/** One page of results plus everything the pager needs. Mirrors the API's PagedResult. */
export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly totalCount: number;
  readonly page: number;
  readonly pageSize: number;
  readonly totalPages: number;
  readonly hasPreviousPage: boolean;
  readonly hasNextPage: boolean;
}

export interface StoryQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly search: string;
}
