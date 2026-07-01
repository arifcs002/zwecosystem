import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { forkJoin } from 'rxjs';
import { CategoryService, Category } from '../../../services/category/category.service';
import { ProductService, Product } from '../../../services/product/product.service';
import { SettingsService } from '../../../services/settings/settings.service';
import { ImgUrlPipe } from '../../../pipes/img-url.pipe';
import { ProductGroup, groupProducts, groupForProductId } from '../../../utils/product-group.util';

@Component({
  selector: 'app-dashboard-view',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, ImgUrlPipe],
  templateUrl: './dashboard-view.component.html',
  styleUrl: './dashboard-view.component.css'
})
export class DashboardViewComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private categoryService = inject(CategoryService);
  private productService = inject(ProductService);
  private router = inject(Router);

  allProducts: Product[] = [];
  allCategories: Category[] = [];
  visibleCategories: Category[] = [];

  // Grouped (one card per base product) per selected category.
  featuredGroups: ProductGroup[] = [];
  filteredGroups: ProductGroup[] = [];

  selectedCategory = 'All';
  isLoading = false;
  errorMsg = '';

  storeName = '';
  primaryColor = '#7c3aed';
  logoUrl = '';

  ngOnInit() { this.loadData(); }

  loadData() {
    this.isLoading = true;
    forkJoin({
      settings: this.settingsService.getSettings(),
      categories: this.categoryService.getCategories(),
      products: this.productService.getProducts()
    }).subscribe({
      next: ({ settings, categories, products }) => {
        this.allCategories = categories;
        this.allProducts = products;

        let visibleCategoryIds: string[] = [];
        const orderPrefix = 'category_order_';
        const categoryOrders = new Map<number, number[]>();
        settings.forEach(s => {
          if (s.key === 'visible_dashboard_categories') visibleCategoryIds = s.value ? s.value.split(',').filter(Boolean) : [];
          if (s.key === 'store_name') this.storeName = s.value;
          if (s.key === 'primary_color') this.primaryColor = s.value;
          if (s.key === 'logo_url') this.logoUrl = s.value;
          if (s.key.startsWith(orderPrefix) && s.value) {
            categoryOrders.set(Number(s.key.slice(orderPrefix.length)), s.value.split(',').filter(Boolean).map(Number));
          }
        });

        this.visibleCategories = visibleCategoryIds.length > 0
          ? categories.filter(c => visibleCategoryIds.includes((c.id ?? '').toString()))
          : categories.filter(c => !c.parentId);

        // Same logic as the public storefront homepage: curated order per
        // category if configured, else that category's latest products.
        const seen = new Set<string>();
        const groups: ProductGroup[] = [];
        this.visibleCategories.forEach(cat => {
          const catId = Number(cat.id);
          const order = categoryOrders.get(catId);
          const catGroups = order && order.length > 0
            ? order.map(id => groupForProductId(products, id)).filter((g): g is ProductGroup => !!g)
            : groupProducts(products.filter(p => this.productBelongsToCategory(Number(p.categoryId) || 0, catId, categories))).slice(0, 10);
          catGroups.forEach(g => {
            const key = `${g.baseName}::${g.categoryId}`;
            if (seen.has(key)) return;
            seen.add(key);
            groups.push(g);
          });
        });

        this.featuredGroups = groups;
        this.filteredGroups = [...groups];
        this.isLoading = false;
      },
      error: () => { this.errorMsg = 'Failed to load settings.'; this.isLoading = false; }
    });
  }

  private productBelongsToCategory(productCatId: number, catId: number, allCats: Category[]): boolean {
    if (productCatId === catId) return true;
    const subIds = allCats.filter(c => Number(c.parentId) === catId).map(c => Number(c.id));
    return subIds.some(sid => this.productBelongsToCategory(productCatId, sid, allCats));
  }

  filterByCategory(catName: string) {
    this.selectedCategory = catName;
    this.filteredGroups = catName === 'All'
      ? [...this.featuredGroups]
      : this.featuredGroups.filter(g => g.categoryName === catName);
  }
}
