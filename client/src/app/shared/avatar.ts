import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** Initials on a colour derived from the name, so the same person always looks the same. */
@Component({
  selector: 'app-avatar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `{{ initials() }}`,
  host: {
    '[style.background]': 'color()',
    '[style.width.px]': 'size()',
    '[style.height.px]': 'size()',
    '[style.fontSize.px]': 'size() * 0.42',
    '[attr.title]': 'name()',
    role: 'img',
    '[attr.aria-label]': 'name()',
  },
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      border-radius: 50%;
      color: #fff;
      font-weight: 500;
      flex: none;
      user-select: none;
    }
  `,
})
export class Avatar {
  readonly name = input.required<string>();
  readonly size = input(28);

  readonly initials = computed(() => {
    const parts = this.name().trim().split(/[\s._-]+/).filter(Boolean);
    return ((parts[0]?.[0] ?? '?') + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase();
  });

  readonly color = computed(() => {
    let hash = 0;
    for (const ch of this.name()) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
    return `hsl(${hash % 360} 45% 40%)`;
  });
}
