import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { LabelDto } from '../api/models';

const DARK = '#0a0a0a';

function luminance(hex: string): number {
  const [r, g, b] = [0, 2, 4].map((i) => {
    const channel = parseInt(hex.replace('#', '').slice(i, i + 2), 16) / 255;
    return channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** Text colour (dark or white) with the better WCAG contrast on a background colour. Either choice reaches 4.5:1 for any hue. */
export function readableTextOn(background: string): string {
  const bg = luminance(background);
  const onWhite = 1.05 / (bg + 0.05);
  const onDark = (bg + 0.05) / (luminance(DARK) + 0.05);
  return onDark >= onWhite ? DARK : '#ffffff';
}

@Component({
  selector: 'app-label-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `{{ label().name }}`,
  host: { '[style.background]': 'label().color', '[style.color]': 'textColor()', '[attr.title]': 'label().name' },
  styles: `
    :host {
      display: inline-block;
      padding: 1px 8px;
      border-radius: 10px;
      font-size: 12px;
      font-weight: 500;
      line-height: 18px;
      max-width: 100%;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `,
})
export class LabelChip {
  readonly label = input.required<LabelDto>();

  /** Dark or white, whichever has the higher WCAG contrast on the label's colour. */
  readonly textColor = computed(() => readableTextOn(this.label().color));
}
