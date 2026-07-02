import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { forkJoin } from 'rxjs';
import { CompanyService } from '../../../services/company/company.service';
import { ProductService, Product } from '../../../services/product/product.service';
import { SettingsService } from '../../../services/settings/settings.service';
import { CartService } from '../../../services/cart/cart.service';
import { ImgUrlPipe } from '../../../pipes/img-url.pipe';
import { CartWidgetComponent } from '../cart-widget/cart-widget.component';
import { ProductGroup, groupForProductId, toShopProduct, discountPercent } from '../../../utils/product-group.util';

@Component({
  selector: 'app-product-detail',
  standalone: true,
  imports: [CommonModule, RouterModule, ImgUrlPipe, CartWidgetComponent],
  templateUrl: './product-detail.component.html'
})
export class ProductDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private companyService = inject(CompanyService);
  private productService = inject(ProductService);
  private settingsService = inject(SettingsService);
  cartService = inject(CartService);

  companySlug = '';
  companyName = '';
  companyLogo = '';
  isLoading = true;
  notFound = false;

  group: ProductGroup | null = null;
  selectedVariant: Product | null = null;
  quantity = 1;

  // Product-display settings
  lowStockThreshold = 5;
  enableWhatsapp = false;
  whatsappNumber = '';
  enableCall = false;
  callNumber = '';

  get discount(): number { return this.group ? discountPercent(this.group) : 0; }

  ngOnInit() {
    this.route.paramMap.subscribe(params => {
      this.companySlug = params.get('companySlug') || '';
      const productId = Number(params.get('id'));

      // Resolve the company FIRST so tenant_company_id is in localStorage before
      // the tenant-scoped products/settings calls fire — otherwise the settings
      // endpoint (which requires a tenant) returns an empty list.
      this.companyService.getPublicCompany(this.companySlug).subscribe({
        next: (company) => {
          this.companyName = company.name;
          this.companyLogo = company.logoUrl || '';
          localStorage.setItem('tenant_company_id', company.id.toString());

          forkJoin({
            products: this.productService.getPublicProducts(),
            settings: this.settingsService.getPublicSettings()
          }).subscribe({
            next: ({ products, settings }) => {
              const v = (k: string) => settings.find(s => s.key === k)?.value || '';
              if (v('low_stock_threshold') && !isNaN(+v('low_stock_threshold'))) this.lowStockThreshold = +v('low_stock_threshold');
              this.enableWhatsapp = v('enable_whatsapp_order') === 'true';
              this.whatsappNumber = v('whatsapp_number');
              this.enableCall = v('enable_call_order') === 'true';
              this.callNumber = v('call_number');

              this.group = groupForProductId(products, productId);
              if (!this.group) { this.notFound = true; this.isLoading = false; return; }

              this.selectedVariant =
                this.group.variants.find(v => v.id === productId && v.stockQuantity > 0) ||
                this.group.variants.find(v => v.stockQuantity > 0) ||
                this.group.variants[0];
              this.isLoading = false;
            },
            error: () => { this.notFound = true; this.isLoading = false; }
          });
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
    this.router.navigate(['/', this.companySlug, 'checkout']);
  }

  goBack() { this.router.navigate(['/', this.companySlug]); }
}
