import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { PagedResult, Story, StoryQuery } from '../models/story';

/** The only place that knows the API's URL shape. */
@Injectable({ providedIn: 'root' })
export class StoryApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/api/stories`;

  getNewest(query: StoryQuery): Observable<PagedResult<Story>> {
    let params = new HttpParams()
      .set('page', query.page)
      .set('pageSize', query.pageSize);

    // Omitted rather than sent empty, so the API's "no filter" path is used.
    const search = query.search.trim();
    if (search.length > 0) {
      params = params.set('search', search);
    }

    return this.http.get<PagedResult<Story>>(`${this.baseUrl}/newest`, { params });
  }

  getById(id: number): Observable<Story> {
    return this.http.get<Story>(`${this.baseUrl}/${id}`);
  }
}
