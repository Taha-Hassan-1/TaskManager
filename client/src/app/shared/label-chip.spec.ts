import { describe, expect, it } from 'vitest';
import { readableTextOn } from './label-chip';

/** WCAG contrast ratio between two hex colours. */
function contrast(a: string, b: string): number {
  const lum = (hex: string) => {
    const [r, g, bl] = [0, 2, 4].map((i) => {
      const c = parseInt(hex.replace('#', '').slice(i, i + 2), 16) / 255;
      return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
    });
    return 0.2126 * r + 0.7152 * g + 0.0722 * bl;
  };
  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

describe('readableTextOn', () => {
  // The eight colours the label picker offers, plus the extremes.
  const colours = ['#e53935', '#fb8c00', '#fdd835', '#43a047', '#1e88e5', '#8e24aa', '#6d4c41', '#546e7a', '#000000', '#ffffff', '#808080'];

  it.each(colours)('reaches 4.5:1 on %s', (background) => {
    expect(contrast(readableTextOn(background), background)).toBeGreaterThanOrEqual(4.5);
  });

  it('uses white on dark colours and dark text on light ones', () => {
    expect(readableTextOn('#000000')).toBe('#ffffff');
    expect(readableTextOn('#ffffff')).toBe('#0a0a0a');
  });
});
