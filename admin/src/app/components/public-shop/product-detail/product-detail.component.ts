import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { StorefrontData, StorefrontService } from '../../../services/storefront/storefront.service';
import { Product } from '../../../services/product/product.service';
import { CartService } from '../../../services/cart/cart.service';
import { WishlistService } from '../../../services/wishlist/wishlist.service';
import { ImgUrlPipe } from '../../../pipes/img-url.pipe';
import { ImgFallbackDirective } from '../../../directives/img-fallback.directive';
import { ProductGroup, groupProducts, groupForProductId, toShopProduct, discountPercent } from '../../../utils/product-group.util';
import { TenantService } from '../../../services/tenant/tenant.service';
import { ShopHeaderComponent } from '../shared/shop-header.component';
import { ShopFooterComponent } from '../shared/shop-footer.component';
import { ShopCardComponent, groupKey } from '../shared/shop-card.component';

@Component({
  selector: 'app-product-detail',
  standalone: true,
  imports: [CommonModule, RouterModule, ImgUrlPipe, ImgFallbackDirective, ShopHeaderComponent, ShopFooterComponent, ShopCardComponent],
  templateUrl: './product-detail.component.html'
})
export class ProductDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private storefront = inject(StorefrontService);
  private tenant = inject(TenantService);
  wishlist = inject(WishlistService);
  cartService = inject(CartService);

  companySlug = '';
  store: StorefrontData | null = null;
  get companyName() { return this.store?.company.name || ''; }
  isLoading = true;
  notFound = false;

  allGroups: ProductGroup[] = [];
  group: ProductGroup | null = null;
  related: ProductGroup[] = [];
  selectedVariant: Product | null = null;
  quantity = 1;

  // Product-display settings
  lowStockThreshold = 5;
  newBadgeDays = 0;
  enableWhatsapp = false;
  whatsappNumber = '';
  enableCall = false;
  callNumber = '';

  get discount(): number { return this.group ? discountPercent(this.group) : 0; }
  get wished(): boolean { return !!this.group && this.wishlist.has(groupKey(this.group)); }
  get inStock(): boolean { return !!this.selectedVariant && this.selectedVariant.stockQuantity > 0; }

  ngOnInit() {
    this.route.paramMap.subscribe(params => {
      this.companySlug = this.tenant.getSlug(this.route);
      const productId = Number(params.get('id'));
      this.isLoading = true; this.notFound = false; this.quantity = 1;
      window.scrollTo({ top: 0 });

      this.storefront.getStorefront(this.companySlug).subscribe({
        next: (data) => {
          this.store = data;
          localStorage.setItem('tenant_company_id', data.company.id.toString());
          this.wishlist.use(data.company.id);
          const v = (k: string) => data.settings.find(s => s.key === k)?.value || '';
          if (v('low_stock_threshold') && !isNaN(+v('low_stock_threshold'))) this.lowStockThreshold = +v('low_stock_threshold');
          if (v('new_badge_days') && !isNaN(+v('new_badge_days'))) this.newBadgeDays = +v('new_badge_days');
          this.enableWhatsapp = v('enable_whatsapp_order') === 'true';
          this.whatsappNumber = v('whatsapp_number');
          this.enableCall = v('enable_call_order') === 'true';
          this.callNumber = v('call_number');

          this.allGroups = groupProducts(data.products);
          this.group = groupForProductId(data.products, productId);
          if (!this.group) { this.notFound = true; this.isLoading = false; return; }

          this.selectedVariant =
            this.group.variants.find(x => x.id === productId && x.stockQuantity > 0) ||
            this.group.variants.find(x => x.stockQuantity > 0) ||
            this.group.variants[0];

          const me = groupKey(this.group);
          const sameCat = this.allGroups.filter(g => groupKey(g) !== me && g.categoryId === this.group!.categoryId && g.totalStock > 0);
          const others = this.allGroups.filter(g => groupKey(g) !== me && g.categoryId !== this.group!.categoryId && g.totalStock > 0);
          this.related = [...sameCat, ...others].slice(0, 4);
          this.isLoading = false;
          document.title = `${this.group.baseName} — ${data.company.name}`;
        },
        error: () => { this.notFound = true; this.isLoading = false; }
      });
    });
  }

  selectVariant(v: Product) {
    if (v.stockQuantity <= 0) return;
    this.selectedVariant = v;
    this.quantity = 1;
  }

  changeQuantity(delta: number) {
    if (!this.selectedVariant) return;
    const next = this.quantity + delta;
    if (next >= 1 && next <= this.selectedVariant.stockQuantity) this.quantity = next;
  }

  stockLabel(stock: number): string {
    if (stock <= 0) return 'Out of Stock';
    if (stock <= this.lowStockThreshold) return `Only ${stock} left`;
    return 'In Stock';
  }

  toggleWish() {
    if (!this.group || !this.selectedVariant) return;
    this.wishlist.toggle({
      key: groupKey(this.group), productId: this.selectedVariant.id, name: this.group.baseName,
      price: this.group.price, image: this.group.imageUrl || '', categoryName: this.group.categoryName
    });
  }

  share() {
    const url = window.location.href;
    const title = this.group?.baseName || 'Product';
    if (navigator.share) { navigator.share({ title, url }).catch(() => { /* cancelled */ }); return; }
    navigator.clipboard?.writeText(url).catch(() => { /* blocked */ });
  }

  // Pre-filled WhatsApp / phone order for the selected variant.
  orderOnWhatsApp() {
    if (!this.whatsappNumber || !this.selectedVariant) return;
    const num = this.whatsappNumber.replace(/[^0-9]/g, '');
    const text = encodeURIComponent(`Hi, I'd like to order: ${this.selectedVariant.name} (৳${this.selectedVariant.price}) x ${this.quantity}`);
    window.open(`https://wa.me/${num}?text=${text}`, '_blank');
  }

  callToOrder() {
    if (this.callNumber) window.location.href = `tel:${this.callNumber.replace(/\s/g, '')}`;
  }

  addToCart() {
    if (!this.selectedVariant || this.selectedVariant.stockQuantity <= 0) return;
    this.cartService.addToCart(toShopProduct(this.selectedVariant), this.quantity);
  }

  buyNow() {
    if (!this.selectedVariant || this.selectedVariant.stockQuantity <= 0) return;
    this.addToCart();
    this.cartService.isCartOpen = false;
    this.router.navigate(this.tenant.routeSegments(this.companySlug, 'checkout'));
  }

  link(...parts: string[]): any[] { return this.tenant.routeSegments(this.companySlug, ...parts); }
  goBack() { this.router.navigate(this.link()); }
}
