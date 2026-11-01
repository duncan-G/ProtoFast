import { isDevMode } from '@angular/core';
import { Routes } from '@angular/router';
import { consoleGuard, FORBIDDEN_ROUTE } from '../admin-kit';

export const routes: Routes = [
  FORBIDDEN_ROUTE,
  {
    path: '',
    canActivateChild: [consoleGuard],
    children: [
      {
        path: '',
        loadComponent: () => import('./pages/overview/overview').then((m) => m.Overview),
      },
      {
        path: 'stories',
        loadComponent: () => import('./pages/stories/stories').then((m) => m.Stories),
      },
      {
        path: 'stories/:id',
        loadComponent: () => import('./pages/story/story').then((m) => m.Story),
      },
      {
        path: 'stories/:id/world',
        loadComponent: () => import('./pages/world/world').then((m) => m.StoryWorld),
      },
      {
        path: 'runs',
        loadComponent: () => import('./pages/runs/runs').then((m) => m.Runs),
      },
      {
        path: 'runs/:id',
        loadComponent: () => import('./pages/run/run').then((m) => m.Run),
      },
      {
        path: 'families',
        loadComponent: () => import('./pages/families/families').then((m) => m.Families),
      },
      {
        path: 'families/:family',
        loadComponent: () => import('./pages/family/family').then((m) => m.Family),
      },
      {
        path: 'world-models',
        loadComponent: () => import('./pages/world-models/world-models').then((m) => m.WorldModels),
      },
      {
        path: 'world-models/:id',
        loadComponent: () => import('./pages/world-model/world-model').then((m) => m.WorldModel),
      },
      {
        path: 'history',
        canMatch: [() => isDevMode()],
        loadComponent: () => import('./pages/history/history').then((m) => m.History),
      },
      {
        path: 'history/:id',
        canMatch: [() => isDevMode()],
        loadComponent: () => import('./pages/history-run/history-run').then((m) => m.HistoryRun),
      },
      {
        path: 'executors/:id/:version',
        loadComponent: () => import('./pages/executor/executor').then((m) => m.Executor),
      },
      {
        path: 'skills/:id/:version',
        loadComponent: () => import('./pages/skill/skill').then((m) => m.Skill),
      },
      {
        path: '**',
        loadComponent: () => import('../admin-kit/pages/not-found').then((m) => m.NotFound),
      },
    ],
  },
];
