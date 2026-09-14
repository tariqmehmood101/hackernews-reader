import { Routes } from '@angular/router';

import { StoryList } from './features/stories/story-list/story-list';

export const routes: Routes = [
  { path: '', component: StoryList, title: 'Newest stories · Hacker News' },
  { path: '**', redirectTo: '' },
];
