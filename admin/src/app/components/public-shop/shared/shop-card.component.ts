import { Component, Input, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { ImgUrlPipe } from '../../../pipes/img-url.pipe';
import { ImgFallbackDirective } from '../../../directives/img-fallback.directive';
import { ProductGroup, discountPercent, toShopProduct } from '../../../utils/product-group.util';
import { CartService } from '../../../services/cart/cart.service';
import { WishlistService } from '../../../services/wishlist/wishlist.service';
import { TenantService } from '../../../services/tenant/tenant.service';

export function groupKey(g: ProductGroup): string { return `${g.baseName}::${g.categoryId ?? ''}`; }

// One product card used everywhere a list of products is shown (shop page, wishlist,
// related products): image with fallback, sale / new / stock badges, wishlist heart,
// quick add. It owns its own navigation + cart wiring so parents only pass the group.
@Component({
  selector: 'app-shop-card',
  standalone: true,
  imports: [CommonModule, ImgUrlPipe, ImgFallbackDirective],
  template: `
  <div (click)="open()" class="group bg-white rounded-2xl border border-gray-100 shadow-sm hover:shadow-lg hover:-translate-y-0.5 transition-all duration-200 overflow-hidden flex flex-col cursor-pointer h-full">
    <div class="relative aspect-square bg-gray-50 overflow-hidden">
      <img [src]="group.imageUrl ? (group.imageUrl | imgUrl) : placeholder" appImgFallback [alt]="group.baseName" loading="lazy"
           class="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300">
      <div class="absolute top-2 left-2 flex flex-col gap-1 items-start">
        <span *ngIf="discount > 0" class="text-[10px] font-bold px-2 py-0.5 rounded-full bg-rose-500 text-white shadow-sm">-{{ discount }}%</span>
        <span *ngIf="isNew" class="text-[10px] font-bold px-2 py-0.5 rounded-full bg-emerald-500 text-white shadow-sm">New</span>
        <span *ngIf="soldOut" class="text-[10px] font-bold px-2 py-0.5 rounded-full bg-gray-900/85 text-white shadow-sm">Sold out</span>
        <span *ngIf="!soldOut && lowStock" class="text-[10px] font-bold px-2 py-0.5 rounded-full bg-amber-500 text-white shadow-sm">Only {{ group.totalStock }} left</span>
      </div>
      <button type="button" (click)="toggleWish($event)" [attr.aria-label]="wished ? 'Remove from wishlist' : 'Add to wishlist'" [attr.aria-pressed]="wished"
              class="absolute top-2 right-2 w-8 h-8 rounded-full bg-white/90 shadow flex items-center justify-center hover:scale-110 transition"
              [ngClass]="wished ? 'text-rose-500' : 'text-gray-400 hover:text-rose-400'">
        <svg class="w-4 h-4" viewBox="0 0 24 24" [attr.fill]="wished ? 'currentColor' : 'none'" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1-1.1a5.5 5.5 0 0 0-7.8 7.8l1 1.1L12 21l7.8-7.5 1-1.1a5.5 5.5 0 0 0 0-7.8z"/>
        </svg>
      </button>
      <span *ngIf="group.variants.length > 1" class="absolute bottom-2 left-2 text-[10px] font-semibold px-2 py-0.5 rounded-full bg-white/90 text-gray-700 shadow-sm">{{ group.variants.length }} sizes</span>
    </div>
    <div class="p-3 flex-1 flex flex-col">
      <p class="text-[10px] text-indigo-500 font-semibold truncate uppercase tracking-wide">{{ group.categoryName }}</p>
      <h3 class="text-sm font-semibold text-gray-800 line-clamp-2 leading-snug mt-0.5 flex-1">{{ group.baseName }}</h3>
      <div class="flex items-end justify-between mt-2 gap-2">
        <div class="leading-tight">
          <span class="text-base font-bold text-gray-900">৳{{ group.price | number:'1.0-0' }}</span>
          <span *ngIf="discount > 0" class="block text-xs text-gray-400 line-through">৳{{ group.compareAtPrice | number:'1.0-0' }}</span>
        </div>
        <button type="button" (click)="primary($event)" [disabled]="soldOut"
                class="h-9 px-3 rounded-xl text-xs font-semibold transition flex-shrink-0 disabled:bg-gray-100 disabled:text-gray-400 disabled:cursor-not-allowed"
                [ngClass]="soldOut ? '' : 'bg-indigo-600 text-white hover:bg-indigo-700 shadow-sm'">
          {{ soldOut ? 'Sold out' : (group.variants.length > 1 ? 'Options' : 'Add') }}
        </button>
      </div>
    </div>
  </div>`
})
export class ShopCardComponent {
  private router = inject(Router);
  private cart = inject(CartService);
  private wishlist = inject(WishlistService);
  private tenant = inject(TenantService);

  @Input({ required: true }) group!: ProductGroup;
  @Input() slug = '';
  @Input() lowStockThreshold = 5;
  @Input() newBadgeDays = 0;

  readonly placeholder = 'data:image/svg+xml;utf8,' + encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120"><rect width="120" height="120" fill="#f3f4f6"/><g fill="none" stroke="#c4c8d0" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><rect x="30" y="38" width="60" height="44" rx="6"/><circle cx="48" cy="54" r="5"/><path d="M34 78l18-16 12 10 10-8 12 14"/></g></svg>');

  get discount() { return discountPercent(this.group); }
  get soldOut() { return this.group.totalStock <= 0; }
  get lowStock() { return this.group.totalStock > 0 && this.group.totalStock <= this.lowStockThreshold; }
  get wished() { return this.wishlist.has(groupKey(this.group)); }
  get isNew(): boolean {
    if (!this.newBadgeDays || !this.group.createdDate) return false;
    return Date.now() - new Date(this.group.createdDate).getTime() <= this.newBadgeDays * 86400000;
  }

  private firstVariant() { return this.group.variants.find(v => v.stockQuantity > 0) || this.group.variants[0]; }

  open() {
    this.router.navigate(this.tenant.routeSegments(this.slug, 'product', this.firstVariant().id));
  }

  primary(e: Event) {
    e.stopPropagation();
    if (this.soldOut) return;
    if (this.group.variants.length > 1) { this.open(); return; }
    this.cart.addToCart(toShopProduct(this.group.variants[0]), 1);
  }

  toggleWish(e: Event) {
    e.stopPropagation();
    this.wishlist.toggle({
      key: groupKey(this.group),
      productId: this.firstVariant().id,
      name: this.group.baseName,
      price: this.group.price,
      image: this.group.imageUrl || '',
      categoryName: this.group.categoryName
    });
  }
}
