import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ProductCardComponent } from '../product-card/product-card.component';
import { ProductGroup } from '../../../../utils/product-group.util';

// A titled section of product cards. `grid` mode wraps into a multi-column grid;
// otherwise cards scroll horizontally (GhorerBazar-style). Highlight paints a
// soft tinted background for campaign/deal sections.
@Component({
  selector: 'app-product-carousel',
  standalone: true,
  imports: [CommonModule, ProductCardComponent],
  templateUrl: './product-carousel.component.html',
  styleUrl: './product-carousel.component.css'
})
export class ProductCarouselComponent {
  @Input() title = '';
  @Input() groups: ProductGroup[] = [];
  @Input() highlight = false;
  @Input() grid = false;
  @Input() columns = 4;
  @Input() showViewAll = false;
  @Input() lowStockThreshold = 5;
  @Input() newBadgeDays = 0;

  @Output() add = new EventEmitter<ProductGroup>();
  @Output() open = new EventEmitter<ProductGroup>();
  @Output() viewAll = new EventEmitter<void>();
}
