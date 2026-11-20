import { Routes } from '@angular/router';
import { authGuard, boardGuard, guestGuard } from './core/auth/guards';
import { BoardSync } from './features/board/board-sync';
import { BoardStore } from './features/board/board.store';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'boards' },
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'register',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/register-page').then((m) => m.RegisterPage),
  },
  {
    path: 'boards',
    canActivate: [authGuard],
    loadComponent: () => import('./features/boards/boards-page').then((m) => m.BoardsPage),
  },
  {
    // The card drawer is a child route, so it opens over the board and shares its store.
    path: 'boards/:id',
    canActivate: [authGuard, boardGuard],
    providers: [BoardStore, BoardSync],
    loadComponent: () => import('./features/board/board-page').then((m) => m.BoardPage),
    children: [
      {
        path: 'cards/:cardId',
        loadComponent: () => import('./features/board/card-drawer').then((m) => m.CardDrawer),
      },
    ],
  },
  { path: '**', redirectTo: 'boards' },
];
