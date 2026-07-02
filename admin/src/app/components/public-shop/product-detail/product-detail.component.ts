import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { forkJoin } from 'rxjs';
import { CompanyService } from '../../../services/company/company.service';
import { ProductService, Product } from '../../../services/product/product.service';
import { CartService } from '../../../services/cart/cart.service';
import { ImgUrlPipe } from '../../../pipes/img-url.pipe';
import { CartWidgetComponent } from '../cart-widget/cart-widget.component';
import { ProductGroup, groupForProductId, toShopProduct } from '../../../utils/product-group.util';

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
  cartService = inject(CartService);

  companySlug = '';
  companyName = '';
  companyLogo = '';
  isLoading = true;
  notFound = false;

  group: ProductGroup | null = null;
  selectedVariant: Product | null = null;
  quantity = 1;

  ngOnInit() {
    this.route.paramMap.subscribe(params => {
      this.companySlug = params.get('companySlug') || '';
      const productId = Number(params.get('id'));

      forkJoin({
        company: this.companyService.getPublicCompany(this.companySlug),
        products: this.productService.getPublicProducts()
      }).subscribe({
        next: ({ company, products }) => {
          this.companyName = company.name;
          this.companyLogo = company.logoUrl || '';
          localStorage.setItem('tenant_company_id', company.id.toString());

          this.group = groupForProductId(products, productId);
          if (!this.group) { this.notFound = true; this.isLoading = false; return; }

          // Pre-select the requested variant if it's in stock, else the first in-stock one.
          this.selectedVariant =
            this.group.variants.find(v => v.id === productId && v.stockQuantity > 0) ||
            this.group.variants.find(v => v.stockQuantity > 0) ||
            this.group.variants[0];
          this.isLoading = false;
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
    if (stock <= 5) return `Only ${stock} left`;
    return 'In Stock';
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
