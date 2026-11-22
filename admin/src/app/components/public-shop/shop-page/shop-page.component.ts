import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import { StorefrontData, StorefrontService } from '../../../services/storefront/storefront.service';
import { TenantService } from '../../../services/tenant/tenant.service';
import { ProductGroup, groupProducts } from '../../../utils/product-group.util';
import { ShopHeaderComponent } from '../shared/shop-header.component';
import { ShopFooterComponent } from '../shared/shop-footer.component';
import { ShopCardComponent } from '../shared/shop-card.component';

type Sort = 'newest' | 'price-asc' | 'price-desc' | 'name';

// "All products" page: search, category filter, price range, in-stock toggle, sorting.
// Everything is derived client-side from the cached storefront payload, and the filters
// live in the URL (?q=&cat=&sort=&min=&max=&stock=) so a filtered view can be shared.
@Component({
  selector: 'app-shop-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, ShopHeaderComponent, ShopFooterComponent, ShopCardComponent],
  template: `
  <div class="shop-root min-h-screen bg-gray-50 flex flex-col" style="font-family: 'Outfit', sans-serif;">
    <app-shop-header [slug]="slug" [storeName]="store?.company?.name || ''" [logo]="store?.company?.logoUrl || ''" [groups]="groups"></app-shop-header>

    <div *ngIf="loading" class="flex justify-center py-32 text-gray-400 text-sm">Loading products…</div>
    <div *ngIf="!loading && error" class="text-center py-32 px-4">
      <div class="text-5xl mb-3">🏪</div><p class="font-semibold text-gray-700">Store not found</p>
    </div>

    <main *ngIf="!loading && !error" class="max-w-screen-xl mx-auto w-full px-4 sm:px-6 py-6 flex-1">
      <nav class="text-xs text-gray-400 mb-3 flex items-center gap-1.5" aria-label="Breadcrumb">
        <a [routerLink]="link()" class="hover:text-indigo-600">Home</a><span>›</span><span class="text-gray-600 font-medium">All Products</span>
      </nav>
      <div class="flex flex-wrap items-end justify-between gap-3 mb-4">
        <div>
          <h1 class="text-2xl font-bold text-gray-900">{{ q ? 'Results for “' + q + '”' : 'All Products' }}</h1>
          <p class="text-sm text-gray-500">{{ filtered.length }} {{ filtered.length === 1 ? 'product' : 'products' }}</p>
        </div>
        <div class="flex items-center gap-2">
          <button (click)="filtersOpen = !filtersOpen" class="lg:hidden px-3 py-2 text-sm font-semibold border border-gray-200 bg-white rounded-xl">Filters{{ activeFilters ? ' (' + activeFilters + ')' : '' }}</button>
          <select [(ngModel)]="sort" (ngModelChange)="apply()" aria-label="Sort by" class="px-3 py-2 text-sm border border-gray-200 bg-white rounded-xl focus:outline-none focus:border-indigo-400">
            <option value="newest">Newest</option><option value="price-asc">Price: Low to High</option>
            <option value="price-desc">Price: High to Low</option><option value="name">Name: A–Z</option>
          </select>
        </div>
      </div>

      <div class="grid lg:grid-cols-[15rem_1fr] gap-6">
        <aside [class.hidden]="!filtersOpen" class="lg:block bg-white border border-gray-100 rounded-2xl p-4 h-fit lg:sticky lg:top-24 space-y-5">
          <div>
            <p class="text-[11px] font-bold text-gray-400 uppercase tracking-widest mb-2">Category</p>
            <div class="flex flex-wrap lg:flex-col gap-1.5">
              <button (click)="cat = 0; apply()" class="text-left px-3 py-1.5 rounded-lg text-sm transition" [ngClass]="!cat ? 'bg-indigo-600 text-white font-semibold' : 'bg-gray-50 text-gray-600 hover:bg-gray-100'">All</button>
              <button *ngFor="let c of categories" (click)="cat = c.id; apply()" class="text-left px-3 py-1.5 rounded-lg text-sm transition flex justify-between gap-2"
                      [ngClass]="cat === c.id ? 'bg-indigo-600 text-white font-semibold' : 'bg-gray-50 text-gray-600 hover:bg-gray-100'">
                <span class="truncate">{{ c.name }}</span><span class="opacity-60 text-xs">{{ c.count }}</span>
              </button>
            </div>
          </div>
          <div>
            <p class="text-[11px] font-bold text-gray-400 uppercase tracking-widest mb-2">Price (৳)</p>
            <div class="flex items-center gap-2">
              <input type="number" min="0" [(ngModel)]="minPrice" (change)="apply()" placeholder="Min" aria-label="Minimum price" class="w-full border border-gray-200 rounded-lg px-2.5 py-1.5 text-sm focus:outline-none focus:border-indigo-400">
              <span class="text-gray-300">–</span>
              <input type="number" min="0" [(ngModel)]="maxPrice" (change)="apply()" placeholder="Max" aria-label="Maximum price" class="w-full border border-gray-200 rounded-lg px-2.5 py-1.5 text-sm focus:outline-none focus:border-indigo-400">
            </div>
          </div>
          <label class="flex items-center gap-2 text-sm text-gray-600 cursor-pointer">
            <input type="checkbox" [(ngModel)]="inStockOnly" (ngModelChange)="apply()" class="accent-indigo-600 w-4 h-4"> In stock only
          </label>
          <button *ngIf="activeFilters" (click)="reset()" class="w-full text-sm font-semibold text-indigo-600 hover:underline">Clear all filters</button>
        </aside>

        <section>
          <div *ngIf="filtered.length === 0" class="text-center py-20 bg-white rounded-2xl border border-gray-100">
            <div class="text-5xl mb-3">🔍</div>
            <p class="font-semibold text-gray-700 mb-1">No products found</p>
            <p class="text-sm text-gray-400 mb-4">Try a different search or clear your filters.</p>
            <button (click)="reset()" class="px-5 py-2 bg-indigo-600 text-white text-sm font-semibold rounded-xl hover:bg-indigo-700">Clear filters</button>
          </div>
          <div class="grid grid-cols-2 sm:grid-cols-3 xl:grid-cols-4 gap-3 sm:gap-4">
            <app-shop-card *ngFor="let g of visible; trackBy: trackGroup" [group]="g" [slug]="slug" [lowStockThreshold]="lowStock" [newBadgeDays]="newDays"></app-shop-card>
          </div>
          <div *ngIf="visible.length < filtered.length" class="text-center mt-6">
            <button (click)="more()" class="px-6 py-2.5 border border-gray-200 bg-white rounded-xl text-sm font-semibold text-gray-700 hover:border-indigo-300 hover:text-indigo-600 transition">
              Show more ({{ filtered.length - visible.length }} left)
            </button>
          </div>
        </section>
      </div>
    </main>
    <app-shop-footer [store]="store" [slug]="slug"></app-shop-footer>
  </div>`
})
export class ShopPageComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private tenant = inject(TenantService);
  private storefront = inject(StorefrontService);
  private sub?: Subscription;

  slug = '';
  store: StorefrontData | null = null;
  loading = true;
  error = false;

  groups: ProductGroup[] = [];
  categories: { id: number; name: string; count: number }[] = [];
  filtered: ProductGroup[] = [];
  visible: ProductGroup[] = [];
  pageSize = 24;
  shown = 24;
  lowStock = 5;
  newDays = 0;

  q = ''; cat = 0; sort: Sort = 'newest';
  minPrice: number | null = null; maxPrice: number | null = null; inStockOnly = false;
  filtersOpen = false;

  link(...parts: string[]): any[] { return this.tenant.routeSegments(this.slug, ...parts); }
  trackGroup = (_: number, g: ProductGroup) => g.baseName + '::' + g.categoryId;

  get activeFilters(): number {
    return (this.cat ? 1 : 0) + (this.minPrice != null ? 1 : 0) + (this.maxPrice != null ? 1 : 0) + (this.inStockOnly ? 1 : 0);
  }

  ngOnInit() {
    this.slug = this.tenant.getSlug(this.route);
    this.storefront.getStorefront(this.slug).subscribe({
      next: (data) => {
        this.store = data;
        localStorage.setItem('tenant_company_id', String(data.company.id));
        const val = (k: string) => data.settings.find(s => s.key === k)?.value;
        if (val('low_stock_threshold') && !isNaN(+val('low_stock_threshold')!)) this.lowStock = +val('low_stock_threshold')!;
        if (val('new_badge_days') && !isNaN(+val('new_badge_days')!)) this.newDays = +val('new_badge_days')!;
        this.groups = groupProducts(data.products);
        const counts = new Map<number, number>();
        this.groups.forEach(g => { if (g.categoryId != null) counts.set(Number(g.categoryId), (counts.get(Number(g.categoryId)) || 0) + 1); });
        this.categories = data.categories
          .filter(c => counts.has(Number(c.id)))
          .map(c => ({ id: Number(c.id), name: c.name, count: counts.get(Number(c.id)) || 0 }));
        this.loading = false;
        this.sub = this.route.queryParamMap.subscribe(p => {
          this.q = (p.get('q') || '').trim();
          this.cat = Number(p.get('cat')) || 0;
          this.sort = (['newest', 'price-asc', 'price-desc', 'name'].includes(p.get('sort') || '') ? p.get('sort') : 'newest') as Sort;
          this.minPrice = p.get('min') !== null && p.get('min') !== '' ? Number(p.get('min')) : null;
          this.maxPrice = p.get('max') !== null && p.get('max') !== '' ? Number(p.get('max')) : null;
          this.inStockOnly = p.get('stock') === '1';
          this.compute();
        });
      },
      error: () => { this.error = true; this.loading = false; }
    });
  }

  ngOnDestroy() { this.sub?.unsubscribe(); }

  // Push the current filter state into the URL; the queryParamMap subscription recomputes.
  apply() {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        q: this.q || null, cat: this.cat || null, sort: this.sort !== 'newest' ? this.sort : null,
        min: this.minPrice != null && this.minPrice !== ('' as any) ? this.minPrice : null,
        max: this.maxPrice != null && this.maxPrice !== ('' as any) ? this.maxPrice : null,
        stock: this.inStockOnly ? 1 : null
      },
      queryParamsHandling: 'merge'
    });
  }

  reset() {
    this.q = ''; this.cat = 0; this.minPrice = null; this.maxPrice = null; this.inStockOnly = false; this.sort = 'newest';
    this.router.navigate([], { relativeTo: this.route, queryParams: {} });
  }

  more() { this.shown += this.pageSize; this.visible = this.filtered.slice(0, this.shown); }

  private compute() {
    const q = this.q.toLowerCase();
    let list = this.groups.filter(g =>
      (!q || g.baseName.toLowerCase().includes(q) || g.categoryName.toLowerCase().includes(q)) &&
      (!this.cat || Number(g.categoryId) === this.cat) &&
      (this.minPrice == null || g.price >= this.minPrice) &&
      (this.maxPrice == null || g.price <= this.maxPrice) &&
      (!this.inStockOnly || g.totalStock > 0));
    const by: Record<Sort, (a: ProductGroup, b: ProductGroup) => number> = {
      'newest': (a, b) => (b.createdDate || '').localeCompare(a.createdDate || ''),
      'price-asc': (a, b) => a.price - b.price,
      'price-desc': (a, b) => b.price - a.price,
      'name': (a, b) => a.baseName.localeCompare(b.baseName)
    };
    list = [...list].sort(by[this.sort]);
    this.filtered = list;
    this.shown = this.pageSize;
    this.visible = list.slice(0, this.shown);
  }
}
