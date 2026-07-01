import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { SettingsService, CompanySetting } from '../../../services/settings/settings.service';
import { CategoryService, Category } from '../../../services/category/category.service';
import { ProductService, Product } from '../../../services/product/product.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';
import { ImgUrlPipe } from '../../../pipes/img-url.pipe';
import { ProductGroup, groupProducts, groupForProductId } from '../../../utils/product-group.util';
import { AppVersionService, AppVersion } from '../../../services/app-version/app-version.service';
import { resolveImageUrl } from '../../../utils/image-url.util';

interface CategoryNode {
  id: number;
  name: string;
  parentId: number | null;
  children: CategoryNode[];
  expanded: boolean;
}

const ORDER_KEY_PREFIX = 'category_order_';

@Component({
  selector: 'app-dashboard-config',
  standalone: true,
  imports: [CommonModule, FormsModule, ImgUrlPipe],
  templateUrl: './dashboard-config.component.html',
  styleUrl: './dashboard-config.component.css'
})
export class DashboardConfigComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private categoryService = inject(CategoryService);
  private productService = inject(ProductService);
  private notify = inject(GlobalNotificationService);
  private appVersionService = inject(AppVersionService);

  latestAppVersion: AppVersion | null = null;
  resolveUrl = resolveImageUrl;

  categories: Category[] = [];
  categoryTree: CategoryNode[] = [];
  products: Product[] = [];

  // Which categories/sub-categories appear as their own section on the public storefront.
  selectedCategoryIds: { [id: string]: boolean } = {};
  // Per-category curated + ordered product picks (max MAX_PER_CATEGORY each).
  // A category with no entry here falls back to "latest products" on the storefront.
  categoryOrder: { [id: string]: number[] } = {};
  // Per-category search box state for the product picker.
  categorySearch: { [id: string]: string } = {};
  // Which category's product-picker panel is expanded.
  openPickerId: number | null = null;

  storeName = '';
  storePhone = '';
  primaryColor = '#7c3aed';
  logoUrl = '';
  footerAboutText = '';
  facebookLink = '';
  instagramLink = '';
  twitterLink = '';
  youtubeLink = '';
  whatsappLink = '';
  tiktokLink = '';
  linkedinLink = '';

  isLoading = false;
  saving = false;

  readonly MAX_PER_CATEGORY = 10;

  ngOnInit() {
    this.loadData();
    // Company-staff/super-admin download card — silently absent if nothing's
    // been published yet (anonymous/public visitors never reach this page).
    this.appVersionService.getLatest().subscribe({
      next: (v) => this.latestAppVersion = v,
      error: () => { /* no release published yet */ }
    });
  }

  loadData() {
    this.isLoading = true;
    forkJoin({
      categories: this.categoryService.getCategories(),
      products: this.productService.getProducts()
    }).subscribe({
      next: ({ categories, products }) => {
        this.categories = categories.sort((a, b) => a.name.localeCompare(b.name));
        this.products = products;
        this.buildCategoryTree(this.categories);
        this.loadSettings();
      },
      error: () => { this.isLoading = false; }
    });
  }

  buildCategoryTree(cats: Category[]) {
    const map: { [id: number]: CategoryNode } = {};
    cats.forEach(c => { map[Number(c.id)] = { id: Number(c.id), name: c.name, parentId: c.parentId ?? null, children: [], expanded: true }; });
    const roots: CategoryNode[] = [];
    cats.forEach(c => {
      const node = map[Number(c.id)];
      if (c.parentId && map[Number(c.parentId)]) map[Number(c.parentId)].children.push(node);
      else roots.push(node);
    });
    this.categoryTree = roots;
  }

  loadSettings() {
    this.settingsService.getSettings().subscribe({
      next: (settings: CompanySetting[]) => {
        this.selectedCategoryIds = {};
        this.categoryOrder = {};

        settings.forEach((s: CompanySetting) => {
          if (s.key === 'store_name') this.storeName = s.value;
          if (s.key === 'store_phone') this.storePhone = s.value;
          if (s.key === 'primary_color') this.primaryColor = s.value;
          if (s.key === 'logo_url') this.logoUrl = s.value;
          if (s.key === 'footer_about_text') this.footerAboutText = s.value;
          if (s.key === 'facebook_link') this.facebookLink = s.value;
          if (s.key === 'instagram_link') this.instagramLink = s.value;
          if (s.key === 'twitter_link') this.twitterLink = s.value;
          if (s.key === 'youtube_link') this.youtubeLink = s.value;
          if (s.key === 'whatsapp_link') this.whatsappLink = s.value;
          if (s.key === 'tiktok_link') this.tiktokLink = s.value;
          if (s.key === 'linkedin_link') this.linkedinLink = s.value;
          if (s.key === 'visible_dashboard_categories') {
            (s.value ? s.value.split(',') : []).forEach(id => { if (id) this.selectedCategoryIds[id] = true; });
          }
          if (s.key.startsWith(ORDER_KEY_PREFIX)) {
            const catId = s.key.slice(ORDER_KEY_PREFIX.length);
            const ids = s.value ? s.value.split(',').filter(Boolean).map(Number) : [];
            // Picks saved before product grouping existed may contain two
            // different sizes of the same base product as separate entries —
            // collapse those down to one so the picker doesn't show "aa" twice.
            this.categoryOrder[catId] = this.dedupeByGroup(ids);
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

    const settings: CompanySetting[] = [
      { key: 'store_name', value: this.storeName, groupName: 'GENERAL' },
      { key: 'store_phone', value: this.storePhone, groupName: 'GENERAL' },
      { key: 'primary_color', value: this.primaryColor, groupName: 'THEME' },
      { key: 'logo_url', value: this.logoUrl, groupName: 'THEME' },
      { key: 'footer_about_text', value: this.footerAboutText, groupName: 'FOOTER' },
      { key: 'facebook_link', value: this.facebookLink, groupName: 'SOCIAL' },
      { key: 'instagram_link', value: this.instagramLink, groupName: 'SOCIAL' },
      { key: 'twitter_link', value: this.twitterLink, groupName: 'SOCIAL' },
      { key: 'youtube_link', value: this.youtubeLink, groupName: 'SOCIAL' },
      { key: 'whatsapp_link', value: this.whatsappLink, groupName: 'SOCIAL' },
      { key: 'tiktok_link', value: this.tiktokLink, groupName: 'SOCIAL' },
      { key: 'linkedin_link', value: this.linkedinLink, groupName: 'SOCIAL' },
      { key: 'visible_dashboard_categories', value: selectedCatIds, groupName: 'DASHBOARD' },
    ];

    // Sync an order row for every category so clearing a manual pick list also
    // clears it on the server (an upsert can't infer "delete" from absence).
    this.categories.forEach(cat => {
      const id = String(cat.id);
      settings.push({
        key: `${ORDER_KEY_PREFIX}${id}`,
        value: (this.categoryOrder[id] || []).join(','),
        groupName: 'DASHBOARD'
      });
    });

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

  toggleCategoryExpand(node: CategoryNode) { node.expanded = !node.expanded; }

  toggleCategoryVisible(catId: number) {
    const id = String(catId);
    this.selectedCategoryIds[id] = !this.selectedCategoryIds[id];
    if (!this.selectedCategoryIds[id] && this.openPickerId === catId) this.openPickerId = null;
  }

  togglePicker(catId: number) {
    this.openPickerId = this.openPickerId === catId ? null : catId;
  }

  // Product groups (one card per base product, all sizes folded together)
  // belonging to this category OR any of its sub-categories — lets the admin
  // curate a parent section ("Footwear") from across all its sub-categories.
  productGroupsInCategory(catId: number): ProductGroup[] {
    const descendantIds = this.collectDescendantIds(catId);
    const inCat = this.products.filter(p => p.categoryId != null && descendantIds.has(Number(p.categoryId)));
    return groupProducts(inCat);
  }

  private collectDescendantIds(catId: number): Set<number> {
    const ids = new Set<number>([catId]);
    const children = this.categories.filter(c => Number(c.parentId) === catId);
    children.forEach(c => this.collectDescendantIds(Number(c.id)).forEach(id => ids.add(id)));
    return ids;
  }

  filteredGroupsInCategory(catId: number): ProductGroup[] {
    const q = (this.categorySearch[catId] || '').toLowerCase().trim();
    // A group counts as "picked" if its representative id is already curated.
    const pickedKeys = new Set(
      (this.categoryOrder[String(catId)] || [])
        .map(id => groupForProductId(this.products, id))
        .filter((g): g is ProductGroup => !!g)
        .map(g => `${g.baseName}::${g.categoryId}`)
    );
    const unpicked = this.productGroupsInCategory(catId).filter(g => !pickedKeys.has(`${g.baseName}::${g.categoryId}`));
    return q ? unpicked.filter(g => g.baseName.toLowerCase().includes(q)) : unpicked;
  }

  orderedGroupsInCategory(catId: number): ProductGroup[] {
    const ids = this.categoryOrder[String(catId)] || [];
    return ids.map(id => groupForProductId(this.products, id)).filter((g): g is ProductGroup => !!g);
  }

  private dedupeByGroup(ids: number[]): number[] {
    const seen = new Set<string>();
    const result: number[] = [];
    for (const id of ids) {
      const g = groupForProductId(this.products, id);
      const key = g ? `${g.baseName}::${g.categoryId}` : `id:${id}`;
      if (seen.has(key)) continue;
      seen.add(key);
      result.push(id);
    }
    return result;
  }

  addProductToCategory(catId: number, group: ProductGroup) {
    const id = String(catId);
    const current = this.categoryOrder[id] || [];
    if (current.length >= this.MAX_PER_CATEGORY) {
      this.notify.notify({ type: 'warning', title: 'Max reached', message: `You can pick up to ${this.MAX_PER_CATEGORY} products per category.`, ttlMs: 3000 });
      return;
    }
    this.categoryOrder[id] = [...current, group.variants[0].id];
  }

  removeProductFromCategory(catId: number, group: ProductGroup) {
    // Match by any variant id, not just variants[0] — the stored representative
    // id may not be the first variant once re-sorted by size.
    const id = String(catId);
    const variantIds = new Set(group.variants.map(v => v.id));
    this.categoryOrder[id] = (this.categoryOrder[id] || []).filter(pid => !variantIds.has(pid));
  }

  moveProduct(catId: number, index: number, delta: number) {
    const id = String(catId);
    const arr = [...(this.categoryOrder[id] || [])];
    const newIndex = index + delta;
    if (newIndex < 0 || newIndex >= arr.length) return;
    [arr[index], arr[newIndex]] = [arr[newIndex], arr[index]];
    this.categoryOrder[id] = arr;
  }

  selectedCount(catId: number): number {
    return (this.categoryOrder[String(catId)] || []).length;
  }
}
