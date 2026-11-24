import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { SettingsService, CompanySetting } from '../../../../services/settings/settings.service';
import { CategoryService, Category } from '../../../../services/category/category.service';
import { ProductService, Product } from '../../../../services/product/product.service';
import { ConfirmDialogService } from '../../../../services/confirm-dialog/confirm-dialog.service';
import { GlobalNotificationService } from '../../../../services/global-notification/global-notification.service';
import { ImgUrlPipe } from '../../../../pipes/img-url.pipe';
import { ProductGroup, groupProducts, groupForProductId } from '../../../../utils/product-group.util';
import {
  StorefrontLayout, StorefrontBlock, HeroSlide,
  newBlockId, defaultLayout
} from '../../../../models/storefront-layout.model';

// Named preview themes from the design handoff — persisted as the
// `dashboard_theme` setting and applied live so admins can preview colors
// while configuring, matching the mock's theme picker behavior.
export const DASHBOARD_THEMES: Record<string, { bg: string; accent: string; navBg: string }> = {
  cream:  { bg: '#faf6ef', accent: '#ea580c', navBg: '#2b2118' },
  black:  { bg: '#000000', accent: '#a78bfa', navBg: '#1c1c1c' },
  navy:   { bg: '#0b1a3a', accent: '#38bdf8', navBg: '#142a52' },
  slate:  { bg: '#0f172a', accent: '#2dd4bf', navBg: '#1e293b' },
  orange: { bg: '#fff7ed', accent: '#ea580c', navBg: '#7c2d12' },
  white:  { bg: '#ffffff', accent: '#7c3aed', navBg: '#16181d' },
  forest: { bg: '#0d1f17', accent: '#4ade80', navBg: '#173726' },
  maroon: { bg: '#1a0b0f', accent: '#fb7185', navBg: '#3f1521' },
  royal:  { bg: '#f5f3ff', accent: '#7c3aed', navBg: '#312e81' },
  sunset: { bg: '#fff1e6', accent: '#f97316', navBg: '#7c2d12' }
};

// Inline-editable homepage builder — same interaction style as the design
// handoff (edit directly on what looks like the live preview, one Save button
// persists everything), backed by the SAME data the accordion-style
// EcommerceBuilderComponent already used: StorefrontLayout (storefront_layout
// setting) for hero/category/product blocks, and the nav_categories /
// store_name / logo_url settings for the nav bar and header replica.
@Component({
    selector: 'app-storefront-inline-builder',
    imports: [CommonModule, FormsModule, ImgUrlPipe],
    templateUrl: './storefront-inline-builder.component.html',
    styleUrl: './storefront-inline-builder.component.css'
})
export class StorefrontInlineBuilderComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private categoryService = inject(CategoryService);
  private productService = inject(ProductService);
  private confirmSvc = inject(ConfirmDialogService);
  private notify = inject(GlobalNotificationService);

  isLoading = false;
  saving = false;

  layout: StorefrontLayout = defaultLayout();
  categories: Category[] = [];
  products: Product[] = [];

  // Non-block settings this screen also edits.
  private allSettings: CompanySetting[] = [];
  navCategoryIds: number[] = [];
  storeName = '';
  logoUrl = '';
  themeName = 'cream';
  readonly themeNames = Object.keys(DASHBOARD_THEMES);
  get theme() { return DASHBOARD_THEMES[this.themeName] || DASHBOARD_THEMES['cream']; }

  // Nav "+ Add Category" / product-section picker open state + search text.
  navPickerOpen = false;
  sectionPickerOpen: { [blockId: string]: boolean } = {};
  sectionPickerSearch: { [blockId: string]: string } = {};
  categoryIconsPickerOpen = false;

  ngOnInit() {
    this.isLoading = true;
    forkJoin({
      layout: this.settingsService.getStorefrontLayout(),
      categories: this.categoryService.getCategories(),
      products: this.productService.getProducts(),
      settings: this.settingsService.getSettings()
    }).subscribe({
      next: ({ layout, categories, products, settings }) => {
        this.layout = layout;
        this.categories = categories.sort((a, b) => a.name.localeCompare(b.name));
        this.products = products;
        this.allSettings = settings;
        this.storeName = settings.find(s => s.key === 'store_name')?.value || '';
        this.logoUrl = settings.find(s => s.key === 'logo_url')?.value || '';
        const nav = settings.find(s => s.key === 'nav_categories')?.value;
        this.navCategoryIds = nav ? nav.split(',').filter(Boolean).map(Number) : [];
        this.themeName = settings.find(s => s.key === 'dashboard_theme')?.value || 'cream';
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  // ── Nav category bar ─────────────────────────────────────────────────
  get navCategories(): Category[] {
    return this.navCategoryIds
      .map(id => this.categories.find(c => Number(c.id) === id))
      .filter((c): c is Category => !!c);
  }
  get availableForNav(): Category[] {
    return this.categories.filter(c => !this.navCategoryIds.includes(Number(c.id)));
  }
  removeNavCategory(id: number) {
    this.navCategoryIds = this.navCategoryIds.filter(x => x !== Number(id));
  }
  addNavCategory(id: number) {
    const cid = Number(id);
    if (!this.navCategoryIds.includes(cid)) this.navCategoryIds.push(cid);
    this.navPickerOpen = false;
  }

  // ── Hero slider (first hero_slider block — created if none exists yet) ──
  get heroBlock(): StorefrontBlock {
    let block = this.layout.blocks.find(b => b.type === 'hero_slider');
    if (!block) {
      block = { id: newBlockId(), type: 'hero_slider', enabled: true, slides: [] };
      this.layout.blocks.unshift(block);
    }
    return block;
  }
  get heroSlides(): HeroSlide[] { return this.heroBlock.slides ||= []; }
  addSlide() { this.heroSlides.push({ image: '', title: '', subtitle: '', ctaText: '', ctaLink: '' }); }
  async removeSlide(i: number) {
    const ok = await this.confirmSvc.confirm({ title: 'Remove Slide', message: 'Remove this hero slide?', confirmLabel: 'Remove', danger: true });
    if (ok) this.heroSlides.splice(i, 1);
  }
  uploadSlideImage(event: any, slide: HeroSlide) {
    this.doUpload(event, url => slide.image = url);
  }

  // ── Featured categories (first category_icons block) ─────────────────
  get categoryIconsBlock(): StorefrontBlock {
    let block = this.layout.blocks.find(b => b.type === 'category_icons');
    if (!block) {
      block = { id: newBlockId(), type: 'category_icons', enabled: true, title: 'Featured Categories', categoryIds: [] };
      this.layout.blocks.push(block);
    }
    return block;
  }
  get featuredCategories(): Category[] {
    return (this.categoryIconsBlock.categoryIds || [])
      .map(id => this.categories.find(c => Number(c.id) === id))
      .filter((c): c is Category => !!c);
  }
  get availableForFeatured(): Category[] {
    const ids = this.categoryIconsBlock.categoryIds || [];
    return this.categories.filter(c => !ids.includes(Number(c.id)));
  }
  removeFeaturedCategory(id: number) {
    this.categoryIconsBlock.categoryIds = (this.categoryIconsBlock.categoryIds || []).filter(x => x !== Number(id));
  }
  addFeaturedCategory(id: number) {
    const cid = Number(id);
    this.categoryIconsBlock.categoryIds ||= [];
    if (!this.categoryIconsBlock.categoryIds.includes(cid)) this.categoryIconsBlock.categoryIds.push(cid);
    this.categoryIconsPickerOpen = false;
  }

  // ── Top Selling Products (the one pinned, non-deletable product block) ──
  get topSellingBlock(): StorefrontBlock {
    let block = this.layout.blocks.find(b => b.pinned);
    if (!block) {
      block = { id: newBlockId(), type: 'product_grid', enabled: true, title: 'Top Selling Products', pinned: true, source: 'curated', productIds: [], columns: 4 };
      this.layout.blocks.push(block);
    }
    return block;
  }

  // ── Dynamic sections (every OTHER product_carousel / product_grid block) ─
  get productSections(): StorefrontBlock[] {
    return this.layout.blocks.filter(b => !b.pinned && (b.type === 'product_carousel' || b.type === 'product_grid'));
  }
  addSection() {
    const block: StorefrontBlock = { id: newBlockId(), type: 'product_grid', enabled: true, title: 'New Section', source: 'category', categoryId: null, limit: 12, columns: 4 };
    this.layout.blocks.push(block);
  }
  async removeSection(block: StorefrontBlock) {
    const ok = await this.confirmSvc.confirm({ title: 'Delete Section', message: `Delete the "${block.title || 'Untitled'}" section? This cannot be undone.`, confirmLabel: 'Delete', danger: true });
    if (!ok) return;
    this.layout.blocks = this.layout.blocks.filter(b => b.id !== block.id);
  }
  sectionCategoryChanged(block: StorefrontBlock) {
    // Switching a section to a real category clears any hand-picked list —
    // the two sourcing modes are mutually exclusive.
    if (block.source === 'category') block.productIds = [];
  }

  sectionGroups(block: StorefrontBlock): ProductGroup[] {
    if (block.source === 'category' && block.categoryId != null) {
      return groupProducts(this.products.filter(p => Number(p.categoryId) === Number(block.categoryId)));
    }
    return (block.productIds || [])
      .map(id => groupForProductId(this.products, id))
      .filter((g): g is ProductGroup => !!g);
  }
  availableForSection(block: StorefrontBlock): ProductGroup[] {
    const q = (this.sectionPickerSearch[block.id] || '').toLowerCase().trim();
    const pickedKeys = new Set(this.sectionGroups(block).map(g => `${g.baseName}::${g.categoryId}`));
    let groups = groupProducts(this.products).filter(g => !pickedKeys.has(`${g.baseName}::${g.categoryId}`));
    if (q) groups = groups.filter(g => g.baseName.toLowerCase().includes(q));
    return groups.slice(0, 30);
  }
  addSectionProduct(block: StorefrontBlock, group: ProductGroup) {
    block.productIds ||= [];
    block.productIds.push(group.variants[0].id);
  }
  removeSectionProduct(block: StorefrontBlock, group: ProductGroup) {
    const ids = new Set(group.variants.map(v => v.id));
    block.productIds = (block.productIds || []).filter(id => !ids.has(id));
  }
  toggleSectionPicker(block: StorefrontBlock) {
    this.sectionPickerOpen[block.id] = !this.sectionPickerOpen[block.id];
  }

  // ── Image upload (shared) ─────────────────────────────────────────────
  private doUpload(event: any, apply: (url: string) => void) {
    const file = event.target?.files?.[0];
    if (!file) return;
    this.productService.uploadImage(file, 'other').subscribe({
      next: (res) => apply(res.imageUrl),
      error: () => this.notify.notify({ type: 'error', title: 'Upload failed', message: 'Could not upload the image.', ttlMs: 4000 })
    });
  }

  // ── Save everything (layout blocks + nav categories) in one action ───
  save() {
    this.saving = true;
    const settings: CompanySetting[] = this.allSettings
      .filter(s => s.key !== 'nav_categories' && s.key !== 'storefront_layout' && s.key !== 'dashboard_theme')
      .concat([
        { key: 'nav_categories', value: this.navCategoryIds.join(','), groupName: 'STOREFRONT' },
        { key: 'dashboard_theme', value: this.themeName, groupName: 'STOREFRONT' }
      ]);

    forkJoin({
      nav: this.settingsService.updateSettings(settings),
      layout: this.settingsService.saveStorefrontLayout(this.layout)
    }).subscribe({
      next: () => {
        this.saving = false;
        this.notify.notify({ type: 'success', title: 'Saved', message: 'Dashboard saved. Reload the shop to see changes.', ttlMs: 3500 });
      },
      error: () => {
        this.saving = false;
        this.notify.notify({ type: 'error', title: 'Failed', message: 'Could not save the dashboard.', ttlMs: 5000 });
      }
    });
  }
}
