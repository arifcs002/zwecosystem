import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Testimonial } from '../../../../models/storefront-layout.model';

// Customer review cards. Rating renders as filled/empty stars (1..5).
@Component({
  selector: 'app-testimonials',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="tst-block" *ngIf="items.length">
      <h2 class="sec-title" *ngIf="title">{{ title }}</h2>
      <div class="grid">
        <div *ngFor="let t of items" class="card">
          <div class="stars">
            <span *ngFor="let s of [1,2,3,4,5]" [class.on]="s <= (t.rating || 5)">★</span>
          </div>
          <p class="text">“{{ t.text }}”</p>
          <div class="who">
            <span class="avatar">{{ (t.name || '?').charAt(0) }}</span>
            <span class="name">{{ t.name }}</span>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .sec-title { font-size: 20px; font-weight: 800; color: #1f2430; margin: 0 0 16px; position: relative; padding-left: 12px; }
    .sec-title::before { content: ''; position: absolute; left: 0; top: 4px; bottom: 4px; width: 4px; border-radius: 4px; background: #7c3aed; }
    .grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; }
    @media (max-width: 900px) { .grid { grid-template-columns: repeat(2, 1fr); } }
    @media (max-width: 640px) { .grid { grid-template-columns: 1fr; } }
    .card { background: #fff; border: 1px solid #eef0f4; border-radius: 14px; padding: 18px; }
    .stars { color: #d1d5db; font-size: 15px; letter-spacing: 2px; margin-bottom: 10px; }
    .stars .on { color: #f59e0b; }
    .text { color: #4b5563; font-size: 14px; line-height: 1.55; margin: 0 0 14px; }
    .who { display: flex; align-items: center; gap: 10px; }
    .avatar { width: 34px; height: 34px; border-radius: 50%; background: linear-gradient(135deg,#7c3aed,#6366f1);
      color: #fff; font-weight: 700; display: flex; align-items: center; justify-content: center; font-size: 14px; }
    .name { font-weight: 700; color: #1f2430; font-size: 13px; }
  `]
})
export class TestimonialsComponent {
  @Input() title = '';
  @Input() items: Testimonial[] = [];
}
