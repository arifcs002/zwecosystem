import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService, CompanySetting } from '../../../services/settings/settings.service';
import { CategoryService, Category } from '../../../services/category/category.service';
import { ProductService, Product } from '../../../services/product/product.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';

@Component({
  selector: 'app-dashboard-config',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dashboard-config.component.html',
  styleUrl: './dashboard-config.component.css'
})
export class DashboardConfigComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private categoryService = inject(CategoryService);
  private productService = inject(ProductService);
  private notify = inject(GlobalNotificationService);

  categories: Category[] = [];
  products: Product[] = [];
  filteredProducts: Product[] = [];
  productSearch = '';

  selectedCategoryIds: { [id: string]: boolean } = {};
  selectedProductIds: { [id: string]: boolean } = {};

  storeName = '';
  storePhone = '';
  primaryColor = '#7c3aed';
  logoUrl = '';
  facebookLink = '';
  instagramLink = '';

  isLoading = false;
  saving = false;

  readonly MAX_PRODUCTS = 10;

  get selectedProductCount(): number {
    return Object.values(this.selectedProductIds).filter(Boolean).length;
  }

  ngOnInit() { this.loadData(); }

  loadData() {
    this.isLoading = true;
    let loaded = 0;
    const done = () => { if (++loaded === 2) { this.loadSettings(); } };

    this.categoryService.getCategories().subscribe({
      next: (cats) => { this.categories = cats.sort((a, b) => a.name.localeCompare(b.name)); done(); },
      error: () => done()
    });

    this.productService.getProducts().subscribe({
      next: (prods) => {
        this.products = prods.sort((a, b) => a.name.localeCompare(b.name));
        this.filteredProducts = [...this.products];
        done();
      },
      error: () => done()
    });
  }

  filterProducts() {
    const q = this.productSearch.toLowerCase();
    this.filteredProducts = q
      ? this.products.filter(p => p.name.toLowerCase().includes(q) || (p.category?.name || '').toLowerCase().includes(q))
      : [...this.products];
  }

  toggleProduct(id: string) {
    const current = this.selectedProductIds[id];
    if (!current && this.selectedProductCount >= this.MAX_PRODUCTS) {
      this.notify.notify({ type: 'warning', title: 'Max reached', message: `You can select up to ${this.MAX_PRODUCTS} products.`, ttlMs: 3000 });
      return;
    }
    this.selectedProductIds[id] = !current;
  }

  loadSettings() {
    this.settingsService.getSettings().subscribe({
      next: (settings: CompanySetting[]) => {
        settings.forEach((s: CompanySetting) => {
          if (s.key === 'store_name') this.storeName = s.value;
          if (s.key === 'store_phone') this.storePhone = s.value;
          if (s.key === 'primary_color') this.primaryColor = s.value;
          if (s.key === 'logo_url') this.logoUrl = s.value;
          if (s.key === 'facebook_link') this.facebookLink = s.value;
          if (s.key === 'instagram_link') this.instagramLink = s.value;
          if (s.key === 'visible_dashboard_categories') {
            const ids = s.value ? s.value.split(',') : [];
            this.selectedCategoryIds = {};
            ids.forEach((id: string) => { if (id) this.selectedCategoryIds[id] = true; });
          }
          if (s.key === 'visible_dashboard_products') {
            const ids = s.value ? s.value.split(',') : [];
            this.selectedProductIds = {};
            ids.forEach((id: string) => { if (id) this.selectedProductIds[id] = true; });
          }
        });
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  saveSettings() {
    this.saving = true;
    const selectedCatIds = Object.keys(this.selectedCategoryIds).filter(id => this.selectedCategoryIds[id]).join(',');
    const selectedProdIds = Object.keys(this.selectedProductIds).filter(id => this.selectedProductIds[id]).join(',');

    const settings: CompanySetting[] = [
      { key: 'store_name', value: this.storeName, groupName: 'GENERAL' },
      { key: 'store_phone', value: this.storePhone, groupName: 'GENERAL' },
      { key: 'primary_color', value: this.primaryColor, groupName: 'THEME' },
      { key: 'logo_url', value: this.logoUrl, groupName: 'THEME' },
      { key: 'facebook_link', value: this.facebookLink, groupName: 'SOCIAL' },
      { key: 'instagram_link', value: this.instagramLink, groupName: 'SOCIAL' },
      { key: 'visible_dashboard_categories', value: selectedCatIds, groupName: 'DASHBOARD' },
      { key: 'visible_dashboard_products', value: selectedProdIds, groupName: 'DASHBOARD' },
    ];

    this.settingsService.updateSettings(settings).subscribe({
      next: () => {
        this.saving = false;
        this.notify.notify({ type: 'success', title: 'Saved', message: 'Store configuration saved successfully.', ttlMs: 3000 });
      },
      error: () => {
        this.saving = false;
        this.notify.notify({ type: 'error', title: 'Failed', message: 'Could not save configuration.', ttlMs: 5000 });
      }
    });
  }
}
