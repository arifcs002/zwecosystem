import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ImgUrlPipe } from '../../../../pipes/img-url.pipe';
import { Brand } from '../../../../services/brand/brand.service';

// A row of brand logos (falls back to the brand initial when no logo is set).
// Purely presentational — parent passes the store's brands.
@Component({
  selector: 'app-brand-carousel',
  standalone: true,
  imports: [CommonModule, ImgUrlPipe],
  template: `
    <div class="brand-block" *ngIf="brands.length">
      <h2 class="sec-title" *ngIf="title">{{ title }}</h2>
      <div class="row">
        <div *ngFor="let b of brands" class="brand" [title]="b.name">
          <img *ngIf="b.logoUrl" [src]="b.logoUrl | imgUrl" [alt]="b.name">
          <span *ngIf="!b.logoUrl" class="fallback">{{ b.name.charAt(0) }}</span>
        </div>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .sec-title { font-size: 20px; font-weight: 800; color: #1f2430; margin: 0 0 14px; position: relative; padding-left: 12px; }
    .sec-title::before { content: ''; position: absolute; left: 0; top: 4px; bottom: 4px; width: 4px; border-radius: 4px; background: #7c3aed; }
    .row { display: flex; gap: 16px; overflow-x: auto; padding-bottom: 6px; }
    .brand { flex: 0 0 auto; width: 120px; height: 66px; background: #fff; border: 1px solid #eef0f4; border-radius: 12px;
      display: flex; align-items: center; justify-content: center; padding: 10px; }
    .brand img { max-width: 100%; max-height: 100%; object-fit: contain; }
    .fallback { font-size: 22px; font-weight: 800; color: #7c3aed; }
  `]
})
export class BrandCarouselComponent {
  @Input() title = '';
  @Input() brands: Brand[] = [];
}
