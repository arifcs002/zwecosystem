import { Component, ElementRef, OnInit, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { CartService } from '../../../services/cart/cart.service';
import { TenantService } from '../../../services/tenant/tenant.service';
import { SettingsService } from '../../../services/settings/settings.service';
import { ProductService, Product } from '../../../services/product/product.service';
import { groupProducts, ProductGroup, toShopProduct } from '../../../utils/product-group.util';
import { ImgUrlPipe } from '../../../pipes/img-url.pipe';

@Component({
  selector: 'app-cart-widget',
  standalone: true,
  imports: [CommonModule, FormsModule, ImgUrlPipe],
  templateUrl: './cart-widget.component.html'
})
export class CartWidgetComponent implements OnInit {
  cartService = inject(CartService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private tenant = inject(TenantService);
  private settingsService = inject(SettingsService);
  private productService = inject(ProductService);

  @ViewChild('recoScroll') recoScroll?: ElementRef<HTMLDivElement>;

  deliveryCharge = 0;
  freeDeliveryAbove = 0;
  recommendations: ProductGroup[] = [];
  private allGroups: ProductGroup[] = [];

  ngOnInit() {
    this.settingsService.getPublicSettings().subscribe(settings => {
      const val = (k: string) => settings.find(s => s.key === k)?.value || '';
      this.deliveryCharge = +(val('delivery_charge') || 0) || 0;
      this.freeDeliveryAbove = +(val('free_delivery_above') || 0) || 0;
    });

    this.productService.getPublicProducts().subscribe((products: Product[]) => {
      this.allGroups = groupProducts(products.filter(p => p.stockQuantity > 0));
      this.refreshRecommendations();
    });
  }

  // Excludes whatever's already in the cart, shown as a horizontally
  // scrollable upsell row (matches the "You May Also Like" cart pattern).
  private refreshRecommendations() {
    const cartIds = new Set(this.cartService.cart.map(i => i.product.id));
    this.recommendations = this.allGroups
      .filter(g => !g.variants.some(v => cartIds.has(v.id)))
      .slice(0, 10);
  }

  get remainingForFreeDelivery(): number {
    if (this.freeDeliveryAbove <= 0) return 0;
    return Math.max(0, this.freeDeliveryAbove - this.cartService.cartSubtotal);
  }

  get freeDeliveryProgressPct(): number {
    if (this.freeDeliveryAbove <= 0) return 100;
    return Math.min(100, (this.cartService.cartSubtotal / this.freeDeliveryAbove) * 100);
  }

  get qualifiesForFreeDelivery(): boolean {
    return this.freeDeliveryAbove > 0 && this.cartService.cartSubtotal >= this.freeDeliveryAbove;
  }

  changeQty(item: any, delta: number) {
    this.cartService.updateQuantity(item, delta);
    this.refreshRecommendations();
  }

  removeItem(item: any) {
    this.cartService.removeFromCart(item);
    this.refreshRecommendations();
  }

  addRecommendation(group: ProductGroup) {
    const variant = group.variants.find(v => v.stockQuantity > 0);
    if (!variant) return;
    this.cartService.addToCart(toShopProduct(variant), 1);
    this.cartService.isCartOpen = true;
    this.refreshRecommendations();
  }

  scrollReco(dir: number) {
    this.recoScroll?.nativeElement.scrollBy({ left: dir * 240, behavior: 'smooth' });
  }

  // Checkout is a full page now — close the drawer and route to it.
  goToCheckout() {
    if (this.cartService.cart.length === 0) return;
    const slug = this.tenant.getSlug(this.route);
    this.cartService.isCartOpen = false;
    this.router.navigate(this.tenant.routeSegments(slug, 'checkout'));
  }
}
