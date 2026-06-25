import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
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

  categories: Category[] = [];
  products: Product[] = [];
  filteredProducts: Product[] = [];

  selectedCategory: string = 'All';
  isLoading = false;
  errorMsg = '';
  visibleCategoryIds: string[] = [];

  ngOnInit() {
    this.loadData();
  }

  loadData() {
    this.isLoading = true;
    this.settingsService.getSettings().subscribe({
      next: (settings) => {
        const setting = settings.find(s => s.key === 'visible_dashboard_categories');
        this.visibleCategoryIds = setting && setting.value ? setting.value.split(',') : [];
        this.loadCategoriesAndProducts();
      },
      error: () => {
        this.errorMsg = 'Failed to load store settings.';
        this.isLoading = false;
      }
    });
  }

  loadCategoriesAndProducts() {
    this.categoryService.getCategories().subscribe({
      next: (allCats) => {
        // Only keep categories that are in the configured visible list
        this.categories = allCats.filter(c => this.visibleCategoryIds.includes(c.id || ''));
        
        this.productService.getProducts().subscribe({
          next: (allProds) => {
            // Keep products that belong to the configured visible categories
            this.products = allProds.filter(p => this.visibleCategoryIds.includes(p.categoryId || ''));
            this.filterProducts('All');
            this.isLoading = false;
          },
          error: () => {
            this.errorMsg = 'Failed to load products.';
            this.isLoading = false;
          }
        });
      },
      error: () => {
        this.errorMsg = 'Failed to load categories.';
        this.isLoading = false;
      }
    });
  }

  filterProducts(categoryName: string) {
    this.selectedCategory = categoryName;
    if (categoryName === 'All') {
      this.filteredProducts = [...this.products];
    } else {
      this.filteredProducts = this.products.filter(p => p.category?.name === categoryName);
    }
  }
}
