import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CategoryService, Category } from '../../../services/category/category.service';
import { ProductService, Product } from '../../../services/product/product.service';
import { SettingsService } from '../../../services/settings/settings.service';

@Component({
  selector: 'app-dashboard-view',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dashboard-view.component.html',
  styleUrl: './dashboard-view.component.css'
})
export class DashboardViewComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private categoryService = inject(CategoryService);
  private productService = inject(ProductService);
  private router = inject(Router);

  featuredProducts: Product[] = [];
  allProducts: Product[] = [];
  categories: Category[] = [];
  filteredProducts: Product[] = [];

  selectedCategory = 'All';
  isLoading = false;
  errorMsg = '';

  storeName = '';
  primaryColor = '#7c3aed';
  logoUrl = '';

  visibleCategoryIds: string[] = [];
  visibleProductIds: string[] = [];

  ngOnInit() { this.loadData(); }

  loadData() {
    this.isLoading = true;
    this.settingsService.getSettings().subscribe({
      next: (settings) => {
        settings.forEach(s => {
          if (s.key === 'visible_dashboard_categories') this.visibleCategoryIds = s.value ? s.value.split(',').filter(Boolean) : [];
          if (s.key === 'visible_dashboard_products') this.visibleProductIds = s.value ? s.value.split(',').filter(Boolean) : [];
          if (s.key === 'store_name') this.storeName = s.value;
          if (s.key === 'primary_color') this.primaryColor = s.value;
          if (s.key === 'logo_url') this.logoUrl = s.value;
        });
        this.loadProducts();
      },
      error: () => { this.errorMsg = 'Failed to load settings.'; this.isLoading = false; }
    });
  }

  loadProducts() {
    let loaded = 0;
    const done = () => { if (++loaded === 2) { this.buildView(); this.isLoading = false; } };

    this.categoryService.getCategories().subscribe({
      next: (cats) => {
        this.categories = cats.filter(c => this.visibleCategoryIds.includes((c.id ?? '').toString()));
        done();
      },
      error: () => done()
    });

    this.productService.getProducts().subscribe({
      next: (prods) => { this.allProducts = prods; done(); },
      error: () => done()
    });
  }

  buildView() {
    if (this.visibleProductIds.length > 0) {
      // Specific products selected → show those
      this.featuredProducts = this.allProducts
        .filter(p => this.visibleProductIds.includes(p.id.toString()))
        .slice(0, 10);
      this.filteredProducts = [...this.featuredProducts];
      this.selectedCategory = 'All';
    } else if (this.visibleCategoryIds.length > 0) {
      // Category-based fallback
      this.featuredProducts = this.allProducts.filter(p => this.visibleCategoryIds.includes((p.categoryId ?? '').toString()));
      this.filteredProducts = [...this.featuredProducts];
    } else {
      this.featuredProducts = [];
      this.filteredProducts = [];
    }
  }

  filterByCategory(catName: string) {
    this.selectedCategory = catName;
    if (catName === 'All') {
      this.filteredProducts = [...this.featuredProducts];
    } else {
      this.filteredProducts = this.featuredProducts.filter(p => p.category?.name === catName);
    }
  }

  goToConfig() { this.router.navigate([], { relativeTo: null }); }
}
