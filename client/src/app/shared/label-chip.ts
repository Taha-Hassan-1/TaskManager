import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { LabelDto } from '../api/models';

@Component({
  selector: 'app-label-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `{{ label().name }}`,
  host: { '[style.background]': 'label().color', '[style.color]': 'textColor()' },
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

  /** Black or white, whichever reads better on the label's colour. */
  readonly textColor = computed(() => {
    const hex = this.label().color.replace('#', '');
    const [r, g, b] = [0, 2, 4].map((i) => parseInt(hex.slice(i, i + 2), 16));
    return 0.299 * r + 0.587 * g + 0.114 * b > 150 ? '#1b1b1b' : '#ffffff';
  });
}
