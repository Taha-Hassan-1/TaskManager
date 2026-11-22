import { ChangeDetectionStrategy, Component, ElementRef, output, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

/** "Add a card" at the foot of a column: a button that turns into a one-line form. */
@Component({
  selector: 'app-add-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule],
  template: `
    @if (open()) {
      <form (submit)="$event.preventDefault(); submit()">
        <textarea
          #input
          rows="2"
          maxlength="200"
          placeholder="What needs doing?"
          aria-label="Card title"
          [value]="title()"
          (input)="title.set($any($event.target).value)"
          (keydown.enter)="$event.preventDefault(); submit()"
          (keydown.escape)="close()"
        ></textarea>
        <div class="actions">
          <button mat-flat-button type="submit" [disabled]="!title().trim()">Add card</button>
          <button mat-button type="button" (click)="close()">Cancel</button>
        </div>
      </form>
    } @else {
      <button mat-button class="open" (click)="show()"><mat-icon>add</mat-icon> Add a card</button>
    }
  `,
  styles: `
    :host { display: block; padding: 4px 10px 10px; }
    .open { width: 100%; justify-content: flex-start; color: var(--mat-sys-on-surface-variant); border-radius: 10px; }
    .open:hover { background: rgba(16, 24, 40, 0.05); color: var(--mat-sys-primary); }
    textarea {
      width: 100%; box-sizing: border-box; resize: vertical; padding: 10px 12px; border-radius: 10px; font: inherit;
      border: 1px solid var(--mat-sys-outline-variant); background: #fff; color: inherit;
      box-shadow: 0 1px 2px rgba(16, 24, 40, 0.06);
      &:focus { outline: 2px solid var(--mat-sys-primary); outline-offset: -1px; border-color: transparent; }
    }
    .actions { display: flex; gap: 8px; margin-top: 6px; }
  `,
})
export class AddCard {
  readonly added = output<string>();
  readonly open = signal(false);
  // A signal, not a plain field: clearing it re-renders the textarea even without zone.js.
  readonly title = signal('');
  private readonly input = viewChild<ElementRef<HTMLTextAreaElement>>('input');

  show(): void {
    this.open.set(true);
    // The textarea only exists after the next render.
    queueMicrotask(() => setTimeout(() => this.input()?.nativeElement.focus()));
  }

  close(): void {
    this.open.set(false);
    this.title.set('');
  }

  submit(): void {
    const title = this.title().trim();
    if (!title) return;
    this.added.emit(title);
    // Stay open for the next one. The DOM value is cleared at once as well: rendering the signal is
    // asynchronous, and a fast typist's next keystroke would otherwise land on the old text.
    this.title.set('');
    const field = this.input()?.nativeElement;
    if (field) field.value = '';
  }
}
