import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { StorefrontData, StorefrontService } from '../../../services/storefront/storefront.service';
import { CollectionService, ShopCollection } from '../../../services/collection/collection.service';
import { ReviewService } from '../../../services/review/review.service';
import { TenantService } from '../../../services/tenant/tenant.service';
import { ProductGroup, groupProducts } from '../../../utils/product-group.util';
import { TranslatePipe } from '../../../i18n/translate.pipe';
import { ShopHeaderComponent } from '../shared/shop-header.component';
import { ShopFooterComponent } from '../shared/shop-footer.component';
import { ShopCardComponent, groupKey } from '../shared/shop-card.component';

// One curated collection ("Eid Collection"): its products in the owner's order.
@Component({
  selector: 'app-collection-page',
  standalone: true,
  imports: [TranslatePipe, CommonModule, RouterModule, ShopHeaderComponent, ShopFooterComponent, ShopCardComponent],
  template: `
  <div class="shop-root min-h-screen bg-gray-50 flex flex-col" style="font-family: 'Outfit', sans-serif;">
    <app-shop-header [slug]="slug" [storeName]="store?.company?.name || ''" [logo]="store?.company?.logoUrl || ''" [groups]="allGroups"></app-shop-header>
    <main class="max-w-screen-xl mx-auto w-full px-4 sm:px-6 py-6 flex-1">
      <div *ngIf="loading" class="text-center py-24 text-gray-400 text-sm">{{ 'Loading…' | t }}</div>
      <div *ngIf="!loading && notFound" class="text-center py-24">
        <div class="text-5xl mb-3">🗂️</div>
        <p class="font-semibold text-gray-700 mb-3">{{ 'Collection not found' | t }}</p>
        <a [routerLink]="link('shop')" class="text-sm font-semibold text-indigo-600 hover:underline">{{ 'All Products' | t }} →</a>
      </div>
      <ng-container *ngIf="!loading && collection">
        <nav class="text-xs text-gray-400 mb-3 flex items-center gap-1.5" aria-label="Breadcrumb">
          <a [routerLink]="link()" class="hover:text-indigo-600">{{ 'Home' | t }}</a><span>›</span><span class="text-gray-600 font-medium">{{ collection.name }}</span>
        </nav>
        <h1 class="text-2xl font-bold text-gray-900">{{ collection.name }}</h1>
        <p *ngIf="collection.description" class="text-sm text-gray-500 mt-1 mb-4 max-w-2xl whitespace-pre-line">{{ collection.description }}</p>
        <p class="text-sm text-gray-500 mb-4">{{ (items.length === 1 ? '{n} product' : '{n} products') | t:{ n: items.length } }}</p>
        <div *ngIf="items.length === 0" class="text-center py-16 bg-white rounded-2xl border border-gray-100 text-gray-400 text-sm">{{ 'No products in this category.' | t }}</div>
        <div class="grid grid-cols-2 sm:grid-cols-3 xl:grid-cols-4 gap-3 sm:gap-4">
          <app-shop-card *ngFor="let g of items" [group]="g" [slug]="slug" [lowStockThreshold]="lowStock" [newBadgeDays]="newDays"></app-shop-card>
        </div>
      </ng-container>
    </main>
    <app-shop-footer [store]="store" [slug]="slug"></app-shop-footer>
  </div>`
})
export class CollectionPageComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private tenant = inject(TenantService);
  private storefront = inject(StorefrontService);
  private collections = inject(CollectionService);
  private reviews = inject(ReviewService);

  slug = '';
  store: StorefrontData | null = null;
  collection: ShopCollection | null = null;
  allGroups: ProductGroup[] = [];
  items: ProductGroup[] = [];
  loading = true; notFound = false;
  lowStock = 5; newDays = 0;

  link(...parts: string[]): any[] { return this.tenant.routeSegments(this.slug, ...parts); }

  ngOnInit() {
    this.slug = this.tenant.getSlug(this.route);
    this.route.paramMap.subscribe(p => {
      const cslug = p.get('collectionSlug') || '';
      this.loading = true; this.notFound = false;
      this.storefront.getStorefront(this.slug).subscribe({
        next: (data) => {
          this.store = data;
          localStorage.setItem('tenant_company_id', String(data.company.id));
          this.reviews.ensureSummary(data.company.id);
          const val = (k: string) => data.settings.find(s => s.key === k)?.value;
          if (val('low_stock_threshold') && !isNaN(+val('low_stock_threshold')!)) this.lowStock = +val('low_stock_threshold')!;
          if (val('new_badge_days') && !isNaN(+val('new_badge_days')!)) this.newDays = +val('new_badge_days')!;
          this.allGroups = groupProducts(data.products);
          this.collections.publicOne(cslug).subscribe({
            next: (c) => {
              this.collection = c;
              const byKey = new Map(this.allGroups.map(g => [groupKey(g), g]));
              // Keep the owner's order; skip products that no longer exist or are hidden.
              this.items = c.productKeys.map(k => byKey.get(k)).filter((g): g is ProductGroup => !!g);
              this.loading = false;
              document.title = `${c.name} — ${data.company.name}`;
            },
            error: () => { this.notFound = true; this.loading = false; }
          });
        },
        error: () => { this.notFound = true; this.loading = false; }
      });
    });
  }
}
