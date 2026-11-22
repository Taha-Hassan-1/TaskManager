import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { ThemeService } from './theme';

describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
    TestBed.resetTestingModule();
  });

  it('follows the system until the person chooses', () => {
    const theme = TestBed.inject(ThemeService);
    expect(theme.choice()).toBe('system');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });

  it('cycles system, light, dark, system and sets the attribute', () => {
    const theme = TestBed.inject(ThemeService);
    theme.cycle();
    expect(theme.choice()).toBe('light');
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    theme.cycle();
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    theme.cycle();
    expect(theme.choice()).toBe('system');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });

  it('remembers an explicit choice across a reload, and forgets "system"', () => {
    TestBed.inject(ThemeService).set('dark');
    expect(localStorage.getItem('teamboard-theme')).toBe('dark');

    TestBed.resetTestingModule();
    expect(TestBed.inject(ThemeService).choice()).toBe('dark');

    TestBed.inject(ThemeService).set('system');
    expect(localStorage.getItem('teamboard-theme')).toBeNull();
  });

  it('names the next choice so the button can say what it will do', () => {
    const theme = TestBed.inject(ThemeService);
    expect(theme.next()).toBe('light');
    theme.set('dark');
    expect(theme.next()).toBe('system');
  });
});
