import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ImgUrlPipe } from '../../../../pipes/img-url.pipe';
import { ProductGroup, discountPercent } from '../../../../utils/product-group.util';

// Presentational product card (GhorerBazar-style): image, sale/stock badges,
// dual price (sale + struck-through "was"), and an action button. It emits
// intent to the parent — the shop owns the cart + navigation.
@Component({
  selector: 'app-product-card',
  standalone: true,
  imports: [CommonModule, ImgUrlPipe],
  templateUrl: './product-card.component.html',
  styleUrl: './product-card.component.css'
})
export class ProductCardComponent {
  @Input() group!: ProductGroup;
  @Input() lowStockThreshold = 5;
  @Input() newBadgeDays = 0;

  @Output() add = new EventEmitter<ProductGroup>();   // single-variant quick add
  @Output() open = new EventEmitter<ProductGroup>();  // open detail (multi-variant / card click)

  get discount(): number { return discountPercent(this.group); }
  get isMultiVariant(): boolean { return this.group.variants.length > 1; }
  get soldOut(): boolean { return this.group.totalStock <= 0; }
  get lowStock(): boolean { return this.group.totalStock > 0 && this.group.totalStock <= this.lowStockThreshold; }

  // "New" when the product was created within the configured window (0 = off).
  get isNew(): boolean {
    if (!this.newBadgeDays || !this.group.createdDate) return false;
    const ageMs = Date.now() - new Date(this.group.createdDate).getTime();
    return ageMs <= this.newBadgeDays * 86400000;
  }

  onPrimary(event: Event) {
    event.stopPropagation();
    if (this.soldOut) return;
    if (this.isMultiVariant) this.open.emit(this.group);
    else this.add.emit(this.group);
  }
}
