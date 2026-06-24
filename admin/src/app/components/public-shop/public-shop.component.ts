import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { CompanyService } from '../../services/company/company.service';

interface Product {
  id: string;
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

  companySlug: string = '';
  companyName: string = 'Loading...';
  companyLogo: string = '';
  themeClass: string = 'theme-cyberpunk-teal'; // Default fallback
  isValidStore: boolean | null = null;

  // Mocks
  categories = ['All', 'Men', 'Women', 'Accessories', 'Electronics'];
  selectedCategory = 'All';

  products: Product[] = [
    { id: '1', name: 'Classic White T-Shirt', price: 1200, category: 'Men', stock: 50, image: 'https://images.unsplash.com/photo-1521572163474-6864f9cf17ab?auto=format&fit=crop&q=80&w=400' },
    { id: '2', name: 'Denim Jacket', price: 3500, category: 'Women', stock: 20, image: 'https://images.unsplash.com/photo-1576995853123-5a10305d93c0?auto=format&fit=crop&q=80&w=400' },
    { id: '3', name: 'Leather Wallet', price: 850, category: 'Accessories', stock: 15, image: 'https://images.unsplash.com/photo-1627123424574-724758594e93?auto=format&fit=crop&q=80&w=400' },
    { id: '4', name: 'Wireless Earbuds', price: 2500, category: 'Electronics', stock: 100, image: 'https://images.unsplash.com/photo-1590658268037-6bf12165a8df?auto=format&fit=crop&q=80&w=400' },
    { id: '5', name: 'Running Sneakers', price: 4200, category: 'Men', stock: 30, image: 'https://images.unsplash.com/photo-1542291026-7eec264c27ff?auto=format&fit=crop&q=80&w=400' },
    { id: '6', name: 'Summer Dress', price: 2100, category: 'Women', stock: 25, image: 'https://images.unsplash.com/photo-1572804013309-59a88b7e92f1?auto=format&fit=crop&q=80&w=400' }
  ];

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
      
      this.filterProducts('All');
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
