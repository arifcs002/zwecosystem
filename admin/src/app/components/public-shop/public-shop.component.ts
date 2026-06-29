import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { CompanyService } from '../../services/company/company.service';
import { resolveImageUrl } from '../../utils/image-url.util';
import { CategoryService, Category } from '../../services/category/category.service';
import { ProductService } from '../../services/product/product.service';
import { SettingsService } from '../../services/settings/settings.service';

export interface ShopProduct {
  id: number;
  name: string;
  price: number;
  image: string;
  category: string;
  categoryId: number;
  stock: number;
}

export interface CategorySection {
  id: number;
  name: string;
  products: ShopProduct[];
}

export interface CategoryTree {
  id: number;
  name: string;
  expanded: boolean;
  children: CategoryTree[];
}

interface CartItem { product: ShopProduct; quantity: number; }

@Component({
  selector: 'app-public-shop',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './public-shop.component.html',
  styleUrl: './public-shop.component.css'
})
export class PublicShopComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private companyService = inject(CompanyService);
  private categoryService = inject(CategoryService);
  private productService = inject(ProductService);
  private settingsService = inject(SettingsService);

  companySlug = '';
  companyName = 'Loading...';
  companyLogo = '';
  isValidStore: boolean | null = null;
  isLoading = false;

  // Category tree for left sidebar
  categoryTree: CategoryTree[] = [];
  selectedCategoryId: number | null = null;   // null = home (all sections)

  // Category sections for homepage
  categorySections: CategorySection[] = [];

  // Category detail page (when See More clicked)
  detailCategory: CategorySection | null = null;
  allProductsInCategory: ShopProduct[] = [];

  // Cart
  cart: CartItem[] = [];
  isCartOpen = false;
  isCheckoutOpen = false;
  customerInfo = { name: '', mobile: '', address: '', paymentMethod: 'COD' };

  ngOnInit() {
    this.route.paramMap.subscribe(params => {
      this.companySlug = params.get('companySlug') || '';
      const catId = params.get('categoryId');
      this.selectedCategoryId = catId ? Number(catId) : null;

      this.companyService.getCompanies().subscribe({
        next: (companies: any[]) => {
          const comp = companies.find(c => c.subdomain.toLowerCase() === this.companySlug.toLowerCase());
          if (comp) {
            this.companyName = comp.name;
            this.companyLogo = comp.logoUrl || '';
            this.isValidStore = true;
            localStorage.setItem('tenant_company_id', comp.id);
            this.loadStorefrontData();
          } else {
            this.companyName = 'Store Not Found';
            this.isValidStore = false;
          }
        },
        error: () => { this.isValidStore = false; }
      });
    });
  }

  loadStorefrontData() {
    this.isLoading = true;
    this.settingsService.getSettings().subscribe({
      next: (settings) => {
        const catSetting = settings.find(s => s.key === 'visible_dashboard_categories');
        const visibleCategoryIds = catSetting?.value ? catSetting.value.split(',').filter(Boolean) : [];

        this.categoryService.getCategories().subscribe({
          next: (allCats) => {
            const displayCats = visibleCategoryIds.length > 0
              ? allCats.filter(c => visibleCategoryIds.includes((c.id || '').toString()))
              : allCats;

            // Build category tree (parent/child)
            this.buildCategoryTree(displayCats);

            this.productService.getProducts().subscribe({
              next: (allProds) => {
                const shopProds: ShopProduct[] = allProds.map(p => ({
                  id: p.id,
                  name: p.name,
                  price: p.price,
                  image: resolveImageUrl(p.imageUrl),
                  category: p.category?.name || 'Uncategorized',
                  categoryId: Number(p.categoryId) || 0,
                  stock: p.stockQuantity
                }));

                // Build sections: one per visible category (max 10 products each)
                this.categorySections = displayCats
                  .filter(c => !c.parentId) // top-level only on homepage
                  .map(cat => ({
                    id: Number(cat.id),
                    name: cat.name,
                    products: shopProds
                      .filter(p => this.productBelongsToCategory(p.categoryId, Number(cat.id), displayCats))
                      .slice(0, 10)
                  }))
                  .filter(s => s.products.length > 0);

                // If on category detail route, set detail
                if (this.selectedCategoryId) {
                  const cat = displayCats.find(c => Number(c.id) === this.selectedCategoryId);
                  if (cat) {
                    this.detailCategory = {
                      id: Number(cat.id),
                      name: cat.name,
                      products: shopProds.filter(p =>
                        this.productBelongsToCategory(p.categoryId, Number(cat.id), displayCats)
                      )
                    };
                    this.allProductsInCategory = this.detailCategory.products;
                  }
                }

                this.isLoading = false;
              },
              error: () => { this.isLoading = false; }
            });
          },
          error: () => { this.isLoading = false; }
        });
      },
      error: () => { this.isLoading = false; }
    });
  }

  buildCategoryTree(cats: Category[]) {
    const roots: CategoryTree[] = [];
    const map: { [id: number]: CategoryTree } = {};
    cats.forEach(c => { map[Number(c.id)] = { id: Number(c.id), name: c.name, expanded: false, children: [] }; });
    cats.forEach(c => {
      if (c.parentId && map[Number(c.parentId)]) {
        map[Number(c.parentId)].children.push(map[Number(c.id)]);
      } else {
        roots.push(map[Number(c.id)]);
      }
    });
    this.categoryTree = roots;
  }

  // Check if product belongs to category (including via sub-categories)
  productBelongsToCategory(productCatId: number, catId: number, allCats: Category[]): boolean {
    if (productCatId === catId) return true;
    // Check sub-categories
    const subIds = allCats.filter(c => Number(c.parentId) === catId).map(c => Number(c.id));
    return subIds.some(sid => this.productBelongsToCategory(productCatId, sid, allCats));
  }

  toggleCategoryExpand(node: CategoryTree) {
    node.expanded = !node.expanded;
  }

  navigateToCategory(catId: number) {
    this.selectedCategoryId = catId;
    this.router.navigate(['/', this.companySlug, 'category', catId]);
  }

  seeMore(catId: number) {
    this.navigateToCategory(catId);
  }

  goHome() {
    this.selectedCategoryId = null;
    this.detailCategory = null;
    this.router.navigate(['/', this.companySlug]);
  }

  // Cart
  toggleCart() { this.isCartOpen = !this.isCartOpen; if (this.isCartOpen) this.isCheckoutOpen = false; }

  addToCart(product: ShopProduct) {
    const item = this.cart.find(i => i.product.id === product.id);
    if (item) { if (item.quantity < product.stock) item.quantity++; }
    else this.cart.push({ product, quantity: 1 });
    this.isCartOpen = true;
  }

  updateQuantity(item: CartItem, delta: number) {
    item.quantity += delta;
    if (item.quantity <= 0) this.cart = this.cart.filter(c => c !== item);
    else if (item.quantity > item.product.stock) item.quantity = item.product.stock;
  }

  removeFromCart(item: CartItem) { this.cart = this.cart.filter(c => c !== item); }

  get cartTotalItems() { return this.cart.reduce((s, i) => s + i.quantity, 0); }
  get cartSubtotal() { return this.cart.reduce((s, i) => s + i.product.price * i.quantity, 0); }

  proceedToCheckout() { if (this.cart.length) { this.isCartOpen = false; this.isCheckoutOpen = true; } }
  closeCheckout() { this.isCheckoutOpen = false; }

  placeOrder() {
    if (!this.customerInfo.name || !this.customerInfo.mobile || !this.customerInfo.address) {
      alert('Please fill out all required fields.');
      return;
    }
    alert(`Order placed!\nTotal: ৳${this.cartSubtotal}\nPayment: ${this.customerInfo.paymentMethod}`);
    this.cart = [];
    this.isCheckoutOpen = false;
    this.customerInfo = { name: '', mobile: '', address: '', paymentMethod: 'COD' };
  }
}
