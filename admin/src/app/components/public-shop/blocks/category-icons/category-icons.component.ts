import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface IconCategory { id: number; name: string; }

// A row of featured category shortcuts. Emits the chosen category id; the shop
// owns navigation. Purely presentational — parent passes the already-filtered,
// already-ordered category list.
@Component({
  selector: 'app-category-icons',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="cat-block" *ngIf="categories.length">
      <h2 class="sec-title" *ngIf="title">{{ title }}</h2>
      <div class="row">
        <button *ngFor="let c of categories" class="cat" (click)="select.emit(c.id)">
          <span class="circle">{{ emojiFor(c.name) }}</span>
          <span class="label">{{ c.name }}</span>
        </button>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .sec-title { font-size: 18px; font-weight: 800; color: #1f2430; margin: 0 0 14px; }
    .row { display: flex; gap: 18px; overflow-x: auto; padding-bottom: 6px; scrollbar-width: thin; }
    .cat { background: none; border: 0; cursor: pointer; display: flex; flex-direction: column; align-items: center; gap: 8px; flex: 0 0 auto; width: 84px; }
    .circle { width: 66px; height: 66px; border-radius: 50%; background: linear-gradient(135deg,#f3f0ff,#e9e5ff);
      display: flex; align-items: center; justify-content: center; font-size: 28px; transition: transform .15s, box-shadow .15s; }
    .cat:hover .circle { transform: translateY(-2px); box-shadow: 0 8px 18px rgba(124,58,237,.18); }
    .label { font-size: 12px; font-weight: 600; color: #4b5563; text-align: center; line-height: 1.2;
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 84px; }
  `]
})
export class CategoryIconsComponent {
  @Input() title = '';
  @Input() categories: IconCategory[] = [];
  @Output() select = new EventEmitter<number>();

  // Lightweight keyword→emoji mapping so icons feel relevant without per-category
  // image uploads. Falls back to a generic tag.
  emojiFor(name: string): string {
    const n = (name || '').toLowerCase();
    const map: [string, string][] = [
      ['shoe', '👟'], ['sneaker', '👟'], ['sandal', '🩴'], ['baby', '🍼'], ['kid', '🧸'],
      ['shirt', '👕'], ['cloth', '👗'], ['dress', '👗'], ['bag', '👜'], ['watch', '⌚'],
      ['honey', '🍯'], ['oil', '🫒'], ['ghee', '🧈'], ['date', '🌴'], ['spice', '🌶️'],
      ['nut', '🥜'], ['rice', '🍚'], ['drink', '🥤'], ['beverage', '🥤'], ['phone', '📱'],
      ['electronic', '🔌'], ['home', '🏠'], ['sport', '⚽'], ['book', '📚'], ['toy', '🧸']
    ];
    for (const [k, e] of map) if (n.includes(k)) return e;
    return '🏷️';
  }
}
