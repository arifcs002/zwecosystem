import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { CompanyService } from '../../services/company/company.service';
import { resolveImageUrl } from '../../utils/image-url.util';
import { CategoryService } from '../../services/category/category.service';
import { ProductService } from '../../services/product/product.service';
import { SettingsService } from '../../services/settings/settings.service';

interface Product {
  id: number;
  name: string;
  price: number;
  image: string;
  category: string;
  stock: number;
}

interface CartItem {
  product: Product;
  quantity: number;
}

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

  companySlug: string = '';
  companyName: string = 'Loading...';
  companyLogo: string = '';
  themeClass: string = 'theme-cyberpunk-teal'; // Default fallback
  isValidStore: boolean | null = null;

  categories: string[] = [];
  selectedCategory = 'All';
  products: Product[] = [];
  filteredProducts: Product[] = [];

  // Cart State
  cart: CartItem[] = [];
  isCartOpen = false;
  isCheckoutOpen = false;

  // Checkout Form
  customerInfo = {
    name: '',
    mobile: '',
    address: '',
    paymentMethod: 'COD'
  };

  ngOnInit() {
    this.route.paramMap.subscribe(params => {
      this.companySlug = params.get('companySlug') || '';
      
      this.companyService.getCompanies().subscribe({
        next: (companies: any[]) => {
          const comp = companies.find(c => c.subdomain.toLowerCase() === this.companySlug.toLowerCase());
          if (comp) {
            this.companyName = comp.name;
            this.isValidStore = true;
            localStorage.setItem('tenant_company_id', comp.id);
            if (comp.logoUrl) {
              this.companyLogo = comp.logoUrl;
            }
            // Load categories and products for this company
            this.loadStorefrontData();
          } else {
            this.companyName = 'Store Not Found';
            this.isValidStore = false;
            this.products = [];
            this.filteredProducts = [];
            this.categories = [];
          }
        },
        error: (err: any) => {
          console.error('Failed to load companies:', err);
          this.isValidStore = false;
        }
      });
    });
  }

  loadStorefrontData() {
    this.settingsService.getSettings().subscribe({
      next: (settings) => {
        const catSetting = settings.find(s => s.key === 'visible_dashboard_categories');
        const prodSetting = settings.find(s => s.key === 'visible_dashboard_products');
        const visibleCategoryIds = catSetting?.value ? catSetting.value.split(',').filter(Boolean) : [];
        const visibleProductIds = prodSetting?.value ? prodSetting.value.split(',').filter(Boolean) : [];

        this.categoryService.getCategories().subscribe({
          next: (allCats) => {
            const filteredCats = visibleCategoryIds.length > 0
              ? allCats.filter(c => visibleCategoryIds.includes((c.id || '').toString()))
              : allCats;
            this.categories = ['All', ...filteredCats.map(c => c.name)];

            this.productService.getProducts().subscribe({
              next: (allProds) => {
                let filteredProds: any[];
                if (visibleProductIds.length > 0) {
                  // Specific products selected — show those (preserving config order)
                  filteredProds = visibleProductIds
                    .map(id => allProds.find(p => p.id.toString() === id))
                    .filter((p): p is NonNullable<typeof p> => !!p);
                } else if (visibleCategoryIds.length > 0) {
                  filteredProds = allProds.filter(p => visibleCategoryIds.includes((p.categoryId || '').toString()));
                } else {
                  // No config — show all products
                  filteredProds = allProds;
                }
                this.products = filteredProds.map(p => ({
                  id: p.id,
                  name: p.name,
                  price: p.price,
                  image: resolveImageUrl(p.imageUrl),
                  category: p.category?.name || 'Uncategorized',
                  stock: p.stockQuantity
                }));
                this.filterProducts('All');
              },
              error: (err) => console.error('Failed to load products:', err)
            });
          },
          error: (err) => console.error('Failed to load categories:', err)
        });
      },
      error: (err) => console.error('Failed to load settings:', err)
    });
  }

  filterProducts(category: string) {
    this.selectedCategory = category;
    if (category === 'All') {
      this.filteredProducts = [...this.products];
    } else {
      this.filteredProducts = this.products.filter(p => p.category === category);
    }
  }

  // --- Cart Logic ---
  toggleCart() {
    this.isCartOpen = !this.isCartOpen;
    if (this.isCartOpen) this.isCheckoutOpen = false;
  }

  addToCart(product: Product) {
    const existingItem = this.cart.find(item => item.product.id === product.id);
    if (existingItem) {
      if (existingItem.quantity < product.stock) {
        existingItem.quantity++;
      }
    } else {
      this.cart.push({ product, quantity: 1 });
    }
    this.isCartOpen = true; // Open cart feedback
  }

  updateQuantity(item: CartItem, delta: number) {
    item.quantity += delta;
    if (item.quantity <= 0) {
      this.removeFromCart(item);
    } else if (item.quantity > item.product.stock) {
      item.quantity = item.product.stock;
    }
  }

  removeFromCart(item: CartItem) {
    this.cart = this.cart.filter(c => c.product.id !== item.product.id);
  }

  get cartTotalItems(): number {
    return this.cart.reduce((sum, item) => sum + item.quantity, 0);
  }

  get cartSubtotal(): number {
    return this.cart.reduce((sum, item) => sum + (item.product.price * item.quantity), 0);
  }

  // --- Checkout Logic ---
  proceedToCheckout() {
    if (this.cart.length === 0) return;
    this.isCartOpen = false;
    this.isCheckoutOpen = true;
  }

  closeCheckout() {
    this.isCheckoutOpen = false;
  }

  placeOrder() {
    if (!this.customerInfo.name || !this.customerInfo.mobile || !this.customerInfo.address) {
      alert('Please fill out all required fields.');
      return;
    }
    alert(`Order placed successfully!\nTotal: ৳${this.cartSubtotal}\nPayment Method: ${this.customerInfo.paymentMethod}`);
    this.cart = [];
    this.isCheckoutOpen = false;
    this.customerInfo = { name: '', mobile: '', address: '', paymentMethod: 'COD' };
  }

  navigateToRegister() {
    this.router.navigate(['/company/register']);
  }
}
