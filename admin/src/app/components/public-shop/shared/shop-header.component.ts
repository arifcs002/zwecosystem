import { Component, HostListener, Input, OnInit, inject } from '@angular/core';
import { TranslatePipe } from '../../../i18n/translate.pipe';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { CartWidgetComponent } from '../cart-widget/cart-widget.component';
import { ImgUrlPipe } from '../../../pipes/img-url.pipe';
import { ImgFallbackDirective } from '../../../directives/img-fallback.directive';
import { ProductGroup } from '../../../utils/product-group.util';
import { TenantService } from '../../../services/tenant/tenant.service';
import { WishlistService } from '../../../services/wishlist/wishlist.service';
import { I18nService } from '../../../i18n/i18n.service';
import { AccountService } from '../../../services/account/account.service';

// Storefront header used by every inner page (shop, product, checkout, wishlist, track):
// brand, live product search with suggestions, order tracking, wishlist, cart.
@Component({
  selector: 'app-shop-header',
  standalone: true,
  imports: [TranslatePipe, CommonModule, FormsModule, RouterModule, CartWidgetComponent, ImgUrlPipe, ImgFallbackDirective],
  template: `
  <!-- No backdrop-blur here: a backdrop-filter ancestor would trap the cart drawer's fixed overlay. -->
  <nav class="bg-white shadow-sm border-b border-gray-200 sticky top-0 z-40">
    <div class="max-w-screen-xl mx-auto px-4 h-16 flex items-center gap-3">
      <a [routerLink]="link()" class="flex items-center gap-2.5 flex-shrink-0 cursor-pointer min-w-0">
        <img *ngIf="logo" [src]="logo | imgUrl" appImgFallback class="h-9 w-9 rounded-xl object-contain border border-gray-100 shadow-sm" alt="">
        <div *ngIf="!logo" class="h-9 w-9 rounded-xl bg-gradient-to-br from-indigo-600 to-violet-600 flex items-center justify-center text-white font-bold text-sm shadow-sm">{{ (storeName || '?').charAt(0) }}</div>
        <span class="font-bold text-gray-900 text-lg tracking-tight truncate max-w-[9rem] sm:max-w-xs">{{ storeName }}</span>
      </a>

      <div class="relative flex-1 max-w-xl mx-auto hidden md:block" (click)="$event.stopPropagation()">
        <ng-container *ngTemplateOutlet="searchBox"></ng-container>
      </div>

      <div class="flex items-center gap-0.5 ml-auto">
        <button type="button" (click)="i18n.toggle()" [attr.aria-label]="i18n.lang === 'en' ? 'বাংলায় দেখুন' : 'Switch to English'" [attr.title]="i18n.lang === 'en' ? 'বাংলায় দেখুন' : 'Switch to English'"
                class="px-2.5 py-1.5 mr-0.5 text-xs font-bold text-gray-600 border border-gray-200 rounded-lg hover:border-indigo-300 hover:text-indigo-600 transition">{{ i18n.lang === 'en' ? 'বাং' : 'EN' }}</button>        <a [routerLink]="link('track-order')" [attr.title]="'Track your order' | t" [attr.aria-label]="'Track your order' | t"
           class="p-2.5 text-gray-500 hover:text-indigo-600 rounded-xl hover:bg-indigo-50 transition">
          <svg class="w-5 h-5" fill="none" stroke="currentColor" stroke-width="1.8" viewBox="0 0 24 24" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7l9-4 9 4-9 4-9-4z"/><path d="M3 7v10l9 4 9-4V7"/><path d="M12 11v10"/></svg>
        </a>
        <a [routerLink]="link('account')" [attr.title]="account.profile ? account.profile.name : ('Account' | t)" [attr.aria-label]="'Account' | t"
           class="p-2.5 text-gray-500 hover:text-indigo-600 rounded-xl hover:bg-indigo-50 transition">
          <span *ngIf="account.profile" class="w-5 h-5 rounded-full bg-indigo-600 text-white text-[11px] font-bold flex items-center justify-center">{{ account.profile.name.charAt(0).toUpperCase() }}</span>
          <svg *ngIf="!account.profile" class="w-5 h-5" fill="none" stroke="currentColor" stroke-width="1.8" viewBox="0 0 24 24" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="8" r="4"/><path d="M4 21c0-4 3.6-7 8-7s8 3 8 7"/></svg>
        </a>        <a [routerLink]="link('wishlist')" [attr.title]="'Wishlist' | t" [attr.aria-label]="'Wishlist' | t"
           class="relative p-2.5 text-gray-500 hover:text-rose-500 rounded-xl hover:bg-rose-50 transition">
          <svg class="w-5 h-5" fill="none" stroke="currentColor" stroke-width="1.8" viewBox="0 0 24 24" stroke-linecap="round" stroke-linejoin="round"><path d="M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1-1.1a5.5 5.5 0 0 0-7.8 7.8l1 1.1L12 21l7.8-7.5 1-1.1a5.5 5.5 0 0 0 0-7.8z"/></svg>
          <span *ngIf="wishlist.count > 0" class="absolute top-1 right-1 min-w-[16px] h-4 px-1 rounded-full bg-rose-500 text-white text-[10px] font-bold flex items-center justify-center">{{ wishlist.count }}</span>
        </a>
        <app-cart-widget></app-cart-widget>
      </div>
    </div>
    <div class="md:hidden px-4 pb-3 relative" (click)="$event.stopPropagation()">
      <ng-container *ngTemplateOutlet="searchBox"></ng-container>
    </div>
  </nav>

  <ng-template #searchBox>
    <form (submit)="submit($event)" role="search" class="relative">
      <svg class="w-4 h-4 text-gray-400 absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none" fill="none" stroke="currentColor" stroke-width="2" viewBox="0 0 24 24" stroke-linecap="round"><circle cx="11" cy="11" r="7"/><path d="M21 21l-4.3-4.3"/></svg>
      <input [(ngModel)]="query" name="q" (ngModelChange)="onType()" (focus)="open = true" autocomplete="off"
             [placeholder]="'Search products…' | t" [attr.aria-label]="'Search products' | t"
             class="w-full bg-gray-100 border border-transparent rounded-xl pl-9 pr-9 py-2 text-sm focus:outline-none focus:bg-white focus:border-indigo-300 transition">
      <button *ngIf="query" type="button" (click)="clear()" [attr.aria-label]="'Clear search' | t" class="absolute right-2.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600 text-xs">✕</button>
    </form>
    <div *ngIf="open && query.trim().length >= 2" class="absolute left-0 right-0 mt-1.5 bg-white rounded-xl shadow-xl border border-gray-100 overflow-hidden z-50 max-h-96 overflow-y-auto">
      <a *ngFor="let g of hits" (click)="go(g)" class="flex items-center gap-3 px-3 py-2.5 hover:bg-gray-50 cursor-pointer">
        <img [src]="g.imageUrl | imgUrl" appImgFallback class="w-10 h-10 rounded-lg object-cover bg-gray-100 flex-shrink-0" alt="">
        <span class="flex-1 min-w-0">
          <span class="block text-sm font-medium text-gray-800 truncate">{{ g.baseName }}</span>
          <span class="block text-[11px] text-gray-400 truncate">{{ g.categoryName }}</span>
        </span>
        <span class="text-sm font-bold text-gray-900">৳{{ g.price | number:'1.0-0' }}</span>
      </a>
      <div *ngIf="hits.length === 0" class="px-4 py-5 text-sm text-gray-400 text-center">No products match “{{ query.trim() }}”</div>
      <a *ngIf="hits.length > 0" (click)="submit()" class="block px-4 py-2.5 text-center text-xs font-semibold text-indigo-600 bg-gray-50 hover:bg-indigo-50 cursor-pointer">{{ 'See all results →' | t }}</a>
    </div>
  </ng-template>`
})
export class ShopHeaderComponent implements OnInit {
  private router = inject(Router);
  private tenant = inject(TenantService);
  wishlist = inject(WishlistService);
  i18n = inject(I18nService);
  account = inject(AccountService);

  @Input() slug = '';
  @Input() storeName = '';
  @Input() logo = '';
  @Input() groups: ProductGroup[] = [];

  query = '';
  open = false;
  hits: ProductGroup[] = [];

  // Show the active search (?q=) in the box when landing on /shop?q=… from a shared link.
  ngOnInit() {
    try { this.query = this.router.parseUrl(this.router.url).queryParams['q'] || ''; } catch { this.query = ''; }
  }

  link(...parts: string[]): any[] { return this.tenant.routeSegments(this.slug, ...parts); }

  @HostListener('document:click') closeOnOutsideClick() { this.open = false; }

  onType() {
    this.open = true;
    const q = this.query.trim().toLowerCase();
    if (q.length < 2) { this.hits = []; return; }
    this.hits = this.groups
      .filter(g => g.baseName.toLowerCase().includes(q) || g.categoryName.toLowerCase().includes(q))
      .slice(0, 6);
  }

  clear() { this.query = ''; this.hits = []; this.open = false; }

  go(g: ProductGroup) {
    const v = g.variants.find(x => x.stockQuantity > 0) || g.variants[0];
    this.open = false; this.query = '';
    this.router.navigate(this.link('product', String(v.id)));
  }

  submit(e?: Event) {
    e?.preventDefault();
    const q = this.query.trim();
    this.open = false;
    this.router.navigate(this.link('shop'), { queryParams: q ? { q } : {} });
  }
}
