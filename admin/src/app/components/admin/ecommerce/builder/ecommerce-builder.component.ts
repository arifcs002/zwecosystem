import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { SettingsService } from '../../../../services/settings/settings.service';
import { CategoryService, Category } from '../../../../services/category/category.service';
import { ProductService, Product } from '../../../../services/product/product.service';
import { GlobalNotificationService } from '../../../../services/global-notification/global-notification.service';
import { ImgUrlPipe } from '../../../../pipes/img-url.pipe';
import { ProductGroup, groupProducts, groupForProductId } from '../../../../utils/product-group.util';
import {
  StorefrontLayout, StorefrontBlock, BlockType, ProductSource,
  BLOCK_META, newBlockId, defaultLayout, HeroSlide, Testimonial
} from '../../../../models/storefront-layout.model';

@Component({
  selector: 'app-ecommerce-builder',
  standalone: true,
  imports: [CommonModule, FormsModule, ImgUrlPipe],
  templateUrl: './ecommerce-builder.component.html',
  styleUrl: './ecommerce-builder.component.css'
})
export class EcommerceBuilderComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private categoryService = inject(CategoryService);
  private productService = inject(ProductService);
  private notify = inject(GlobalNotificationService);

  layout: StorefrontLayout = defaultLayout();
  categories: Category[] = [];
  products: Product[] = [];

  // UI state
  isLoading = false;
  saving = false;
  expandedBlockId: string | null = null;
  showPalette = false;
  // Per-block product-picker search text (keyed by block id).
  pickerSearch: { [blockId: string]: string } = {};

  readonly blockMeta = BLOCK_META;
  readonly blockTypes = Object.keys(BLOCK_META) as BlockType[];
  readonly sources: { value: ProductSource; label: string }[] = [
    { value: 'newest',     label: 'Newest products' },
    { value: 'discounted', label: 'On sale (has compare price)' },
    { value: 'category',   label: 'Specific category' },
    { value: 'curated',    label: 'Hand-picked products' }
  ];

  ngOnInit() {
    this.isLoading = true;
    forkJoin({
      layout: this.settingsService.getStorefrontLayout(),
      categories: this.categoryService.getCategories(),
      products: this.productService.getProducts()
    }).subscribe({
      next: ({ layout, categories, products }) => {
        this.layout = layout;
        this.categories = categories.sort((a, b) => a.name.localeCompare(b.name));
        this.products = products;
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  // ── Block list operations ────────────────────────────────────────────
  meta(type: BlockType) { return this.blockMeta[type]; }

  addBlock(type: BlockType) {
    this.layout.blocks.push(this.makeBlock(type));
    this.showPalette = false;
    this.expandedBlockId = this.layout.blocks[this.layout.blocks.length - 1].id;
  }

  private makeBlock(type: BlockType): StorefrontBlock {
    const id = newBlockId();
    switch (type) {
      case 'hero_slider':
        return { id, type, enabled: true, slides: [{ image: '', title: '', subtitle: '', ctaText: '', ctaLink: '' }] };
      case 'category_icons':
        return { id, type, enabled: true, title: 'Shop by Category', categoryIds: [], maxVisible: 8 };
      case 'product_carousel':
        return { id, type, enabled: true, title: 'New Arrivals', source: 'newest', limit: 10, highlight: false, viewAllLink: '' };
      case 'product_grid':
        return { id, type, enabled: true, title: 'Products', source: 'newest', limit: 12, columns: 4 };
      case 'promo_banner':
        return { id, type, enabled: true, image: '', link: '' };
      case 'brand_carousel':
        return { id, type, enabled: true, title: 'Our Brands' };
      case 'testimonials':
        return { id, type, enabled: true, title: 'What Customers Say', items: [{ name: '', text: '', rating: 5 }] };
    }
  }

  removeBlock(index: number) {
    this.layout.blocks.splice(index, 1);
  }

  moveBlock(index: number, delta: number) {
    const to = index + delta;
    if (to < 0 || to >= this.layout.blocks.length) return;
    const arr = this.layout.blocks;
    [arr[index], arr[to]] = [arr[to], arr[index]];
  }

  toggleBlock(block: StorefrontBlock) { block.enabled = !block.enabled; }

  toggleExpand(block: StorefrontBlock) {
    this.expandedBlockId = this.expandedBlockId === block.id ? null : block.id;
  }

  blockSummary(block: StorefrontBlock): string {
    switch (block.type) {
      case 'hero_slider':      return `${block.slides?.length || 0} slide(s)`;
      case 'category_icons':   return `${block.categoryIds?.length || 0} categories`;
      case 'product_carousel':
      case 'product_grid':     return this.sourceLabel(block.source) + (block.source === 'category' ? `: ${this.categoryName(block.categoryId)}` : '');
      case 'promo_banner':     return block.image ? 'image set' : 'no image';
      case 'brand_carousel':   return 'all active brands';
      case 'testimonials':     return `${block.items?.length || 0} review(s)`;
      default:                 return '';
    }
  }

  sourceLabel(s?: ProductSource) { return this.sources.find(x => x.value === s)?.label || 'Products'; }
  categoryName(id?: number | null) { return this.categories.find(c => Number(c.id) === Number(id))?.name || '—'; }

  // ── Hero slides ──────────────────────────────────────────────────────
  addSlide(block: StorefrontBlock) {
    (block.slides ||= []).push({ image: '', title: '', subtitle: '', ctaText: '', ctaLink: '' });
  }
  removeSlide(block: StorefrontBlock, i: number) { block.slides?.splice(i, 1); }

  // ── Testimonials ─────────────────────────────────────────────────────
  addTestimonial(block: StorefrontBlock) {
    (block.items ||= []).push({ name: '', text: '', rating: 5 });
  }
  removeTestimonial(block: StorefrontBlock, i: number) { block.items?.splice(i, 1); }

  // ── Category icons picker ────────────────────────────────────────────
  isCategorySelected(block: StorefrontBlock, id: number) {
    return (block.categoryIds || []).includes(Number(id));
  }
  toggleCategory(block: StorefrontBlock, id: number) {
    const cid = Number(id);
    block.categoryIds ||= [];
    const i = block.categoryIds.indexOf(cid);
    if (i >= 0) block.categoryIds.splice(i, 1);
    else block.categoryIds.push(cid);
  }

  // ── Curated product picker (per block) ───────────────────────────────
  allGroups(): ProductGroup[] { return groupProducts(this.products); }

  pickedGroups(block: StorefrontBlock): ProductGroup[] {
    return (block.productIds || [])
      .map(id => groupForProductId(this.products, id))
      .filter((g): g is ProductGroup => !!g);
  }

  availableGroups(block: StorefrontBlock): ProductGroup[] {
    const q = (this.pickerSearch[block.id] || '').toLowerCase().trim();
    const pickedKeys = new Set(this.pickedGroups(block).map(g => `${g.baseName}::${g.categoryId}`));
    let groups = this.allGroups().filter(g => !pickedKeys.has(`${g.baseName}::${g.categoryId}`));
    if (q) groups = groups.filter(g => g.baseName.toLowerCase().includes(q));
    return groups.slice(0, 30);
  }

  addProduct(block: StorefrontBlock, group: ProductGroup) {
    block.productIds ||= [];
    block.productIds.push(group.variants[0].id);
  }
  removeProduct(block: StorefrontBlock, group: ProductGroup) {
    const ids = new Set(group.variants.map(v => v.id));
    block.productIds = (block.productIds || []).filter(id => !ids.has(id));
  }
  moveProduct(block: StorefrontBlock, index: number, delta: number) {
    const arr = block.productIds || [];
    const to = index + delta;
    if (to < 0 || to >= arr.length) return;
    [arr[index], arr[to]] = [arr[to], arr[index]];
  }

  // ── Image upload (hero slides / promo banner) ────────────────────────
  // Angular templates can't hold arrow-function callbacks, so upload targets
  // get dedicated handlers that assign the resulting URL directly.
  uploadSlideImage(event: any, slide: HeroSlide) {
    this.doUpload(event, url => slide.image = url);
  }
  uploadBannerImage(event: any, block: StorefrontBlock) {
    this.doUpload(event, url => block.image = url);
  }
  private doUpload(event: any, apply: (url: string) => void) {
    const file = event.target?.files?.[0];
    if (!file) return;
    this.productService.uploadImage(file, 'other').subscribe({
      next: (res) => apply(res.imageUrl),
      error: () => this.notify.notify({ type: 'error', title: 'Upload failed', message: 'Could not upload the image.', ttlMs: 4000 })
    });
  }

  // ── Save ─────────────────────────────────────────────────────────────
  save() {
    this.saving = true;
    this.settingsService.saveStorefrontLayout(this.layout).subscribe({
      next: () => {
        this.saving = false;
        this.notify.notify({ type: 'success', title: 'Saved', message: 'Storefront layout saved. Reload the shop to see changes.', ttlMs: 3500 });
      },
      error: () => {
        this.saving = false;
        this.notify.notify({ type: 'error', title: 'Failed', message: 'Could not save the layout.', ttlMs: 5000 });
      }
    });
  }
}
