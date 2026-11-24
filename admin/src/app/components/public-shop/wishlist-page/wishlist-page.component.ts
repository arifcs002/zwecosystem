import { Component, OnInit, inject } from '@angular/core';
import { TranslatePipe } from '../../../i18n/translate.pipe';

import { ActivatedRoute, RouterModule } from '@angular/router';
import { StorefrontData, StorefrontService } from '../../../services/storefront/storefront.service';
import { TenantService } from '../../../services/tenant/tenant.service';
import { ReviewService } from '../../../services/review/review.service';
import { WishlistEntry, WishlistService } from '../../../services/wishlist/wishlist.service';
import { ProductGroup, groupProducts } from '../../../utils/product-group.util';
import { ShopHeaderComponent } from '../shared/shop-header.component';
import { ShopFooterComponent } from '../shared/shop-footer.component';
import { ShopCardComponent, groupKey } from '../shared/shop-card.component';

@Component({
    selector: 'app-wishlist-page',
    imports: [TranslatePipe, RouterModule, ShopHeaderComponent, ShopFooterComponent, ShopCardComponent],
    template: `
  <div class="shop-root min-h-screen bg-gray-50 flex flex-col" style="font-family: 'Outfit', sans-serif;">
    <app-shop-header [slug]="slug" [storeName]="store?.company?.name || ''" [logo]="store?.company?.logoUrl || ''" [groups]="groups"></app-shop-header>
    <main class="max-w-screen-xl mx-auto w-full px-4 sm:px-6 py-6 flex-1">
      <nav class="text-xs text-gray-400 mb-3 flex items-center gap-1.5" aria-label="Breadcrumb">
        <a [routerLink]="link()" class="hover:text-indigo-600">{{ 'Home' | t }}</a><span>›</span><span class="text-gray-600 font-medium">{{ 'Wishlist' | t }}</span>
      </nav>
      <h1 class="text-2xl font-bold text-gray-900 mb-1">{{ 'My Wishlist' | t }}</h1>
      <p class="text-sm text-gray-500 mb-5">{{ 'Saved on this device — no account needed.' | t }}</p>
  
      @if (loading) {
        <div class="text-center py-20 text-gray-400 text-sm">{{ 'Loading…' | t }}</div>
      }
  
      @if (!loading && wishlist.count === 0) {
        <div class="text-center py-20 bg-white rounded-2xl border border-gray-100">
          <div class="text-5xl mb-3">🤍</div>
          <p class="font-semibold text-gray-700 mb-1">{{ 'Your wishlist is empty' | t }}</p>
          <p class="text-sm text-gray-400 mb-4">{{ 'Tap the heart on any product to save it for later.' | t }}</p>
          <a [routerLink]="link('shop')" class="inline-block px-5 py-2 bg-indigo-600 text-white text-sm font-semibold rounded-xl hover:bg-indigo-700">{{ 'Browse products' | t }}</a>
        </div>
      }
  
      @if (!loading && wishlist.count > 0) {
        <div class="grid grid-cols-2 sm:grid-cols-3 xl:grid-cols-5 gap-3 sm:gap-4">
          @for (e of wishlist.all; track trackEntry($index, e)) {
            @if (resolve(e); as g) {
              <app-shop-card [group]="g" [slug]="slug"></app-shop-card>
            } @else {
              <div class="bg-white rounded-2xl border border-dashed border-gray-200 p-4 flex flex-col items-center justify-center text-center gap-2">
                <p class="text-sm font-semibold text-gray-500 line-clamp-2">{{ e.name }}</p>
                <p class="text-xs text-gray-400">{{ 'No longer available' | t }}</p>
                <button (click)="wishlist.remove(e.key)" class="text-xs font-semibold text-rose-500 hover:underline">{{ 'Remove' | t }}</button>
              </div>
            }
          }
        </div>
      }
    </main>
    <app-shop-footer [store]="store" [slug]="slug"></app-shop-footer>
  </div>`
})
export class WishlistPageComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private tenant = inject(TenantService);
  private storefront = inject(StorefrontService);
  private reviews = inject(ReviewService);
  wishlist = inject(WishlistService);

  slug = '';
  store: StorefrontData | null = null;
  loading = true;
  groups: ProductGroup[] = [];
  private byKey = new Map<string, ProductGroup>();

  link(...parts: string[]): any[] { return this.tenant.routeSegments(this.slug, ...parts); }
  trackEntry = (_: number, e: WishlistEntry) => e.key;
  resolve(e: WishlistEntry): ProductGroup | null { return this.byKey.get(e.key) || null; }

  ngOnInit() {
    this.slug = this.tenant.getSlug(this.route);
    this.storefront.getStorefront(this.slug).subscribe({
      next: (data) => {
        this.store = data;
        localStorage.setItem('tenant_company_id', String(data.company.id));
        this.reviews.ensureSummary(data.company.id);
        this.wishlist.use(data.company.id);
        this.groups = groupProducts(data.products);
        this.byKey = new Map(this.groups.map(g => [groupKey(g), g]));
        this.loading = false;
      },
      error: () => { this.loading = false; }
    });
  }
}
