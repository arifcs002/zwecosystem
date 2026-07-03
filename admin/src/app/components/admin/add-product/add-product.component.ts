import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { forkJoin, of, Observable } from 'rxjs';
import { SupplierService, Supplier } from '../../../services/supplier/supplier.service';
import { CategoryService, Category } from '../../../services/category/category.service';
import { ProductService, BulkProductLineDto, Product } from '../../../services/product/product.service';
import { PricingTagService, PricingTag } from '../../../services/pricing-tag/pricing-tag.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';
import { SettingsService } from '../../../services/settings/settings.service';
import { InventoryService } from '../../../services/inventory/inventory.service';
import { buildIndentedList, FlatCategory } from '../../../utils/category-tree.util';
import { toSecretCode, DEFAULT_SECRET_MAP } from '../../../utils/secret-code.util';
import { groupProducts, ProductGroup } from '../../../utils/product-group.util';
import { CategoryTreePickerComponent } from '../../shared/category-tree-picker/category-tree-picker.component';
import { ImgUrlPipe } from '../../../pipes/img-url.pipe';

// One product entry in the bulk form. Either a brand-new product (isExisting
// false) or a restock of an already-created product (isExisting true) — the
// two modes use different fields and, on submit, different API calls.
interface ProductLine {
  isExisting: boolean;

  // New-product fields
  name: string;
  wholesalePrice: number;   // buying price
  profitPercent: number | null;
  profitAmount: number | null;
  price: number;            // selling price (auto-computed, editable)
  compareAtPrice: number | null;
  pricingTagId: number | null;
  description: string;
  sizeQuantities: { [size: string]: number };
  imageFile: File | null;
  imagePreview: string | ArrayBuffer | null;
  imageUrl: string;

  // Restock (existing product) fields
  existingGroup: ProductGroup | null;
  existingSearch: string;
  restockQuantities: { [size: string]: number }; // keyed by variant.size (or '' for sizeless)
  restockCost: number | null;
  // Cached picker results — computed once per search/category change instead of
  // every change-detection cycle. Recomputing a fresh array inside *ngFor's
  // template expression makes Angular replace the DOM nodes on every CD tick,
  // which can swallow a click that lands between mousedown and mouseup.
  existingResults: ProductGroup[];
}

@Component({
  selector: 'app-add-product',
  standalone: true,
  imports: [CommonModule, FormsModule, CategoryTreePickerComponent, ImgUrlPipe],
  templateUrl: './add-product.component.html',
  styleUrl: './add-product.component.css'
})
export class AddProductComponent implements OnInit {
  private supplierService = inject(SupplierService);
  private categoryService = inject(CategoryService);
  private productService = inject(ProductService);
  private pricingTagService = inject(PricingTagService);
  private settingsService = inject(SettingsService);
  private inventoryService = inject(InventoryService);
  private router = inject(Router);
  private notify = inject(GlobalNotificationService);

  secretMap = DEFAULT_SECRET_MAP;

  suppliers: Supplier[] = [];
  categories: Category[] = [];
  pricingTags: PricingTag[] = [];
  allProducts: Product[] = [];

  // Shared for all products in this batch.
  supplierId: number | undefined;
  categoryId: number | undefined;

  availableSizes: string[] = [];
  lines: ProductLine[] = [this.newLine()];

  isSubmitting = false;

  ngOnInit() {
    forkJoin({
      suppliers: this.supplierService.getSuppliers(),
      categories: this.categoryService.getCategories(),
      tags: this.pricingTagService.getPricingTags(),
      settings: this.settingsService.getSettings(),
      products: this.productService.getProducts()
    }).subscribe({
      next: ({ suppliers, categories, tags, settings, products }) => {
        this.suppliers = suppliers.sort((a: any, b: any) => a.name.localeCompare(b.name));
        this.categories = categories;
        this.pricingTags = tags.filter(t => t.isActive);
        this.allProducts = products;
        const map = settings.find(s => s.key === 'secret_price_map')?.value;
        if (map && map.length >= 10) this.secretMap = map;
      },
      error: (err) => console.error(err)
    });
  }

  // Existing product groups within the chosen category, for the restock picker.
  get groupsInCategory(): ProductGroup[] {
    if (!this.categoryId) return [];
    const inCat = this.allProducts.filter(p => Number(p.categoryId) === Number(this.categoryId));
    return groupProducts(inCat);
  }

  // Recomputes the cached picker list for a line — call after anything that
  // could change the result set (search text, category, toggling the mode).
  refreshExistingResults(line: ProductLine) {
    const q = (line.existingSearch || '').toLowerCase().trim();
    const groups = this.groupsInCategory;
    line.existingResults = q ? groups.filter(g => g.baseName.toLowerCase().includes(q)) : groups;
  }

  selectExistingGroup(line: ProductLine, group: ProductGroup) {
    line.existingGroup = group;
    line.restockQuantities = {};
    line.existingSearch = group.baseName;
  }

  clearExistingGroup(line: ProductLine) {
    line.existingGroup = null;
    line.existingSearch = '';
    line.restockQuantities = {};
    this.refreshExistingResults(line);
  }

  toggleExisting(line: ProductLine) {
    line.isExisting = !line.isExisting;
    line.existingGroup = null;
    line.existingSearch = '';
    line.restockQuantities = {};
    if (line.isExisting) this.refreshExistingResults(line);
  }

  restockLineTotal(line: ProductLine): number {
    return Object.values(line.restockQuantities).reduce((s, q) => s + (q || 0), 0);
  }

  // Buying price + profit% (and/or flat profit amount) → selling price.
  computePrice(line: ProductLine) {
    const buy = line.wholesalePrice || 0;
    const pct = line.profitPercent || 0;
    const amt = line.profitAmount || 0;
    if (buy > 0 && (pct || amt)) line.price = Math.round(buy * (1 + pct / 100) + amt);
  }

  secretOf(price: number): string { return toSecretCode(price, this.secretMap); }

  // Indented category options (any nesting depth) — the chosen value is the
  // deepest/last-selected category, which is where products are saved.
  get categoryOptions(): FlatCategory[] { return buildIndentedList(this.categories); }

  newLine(): ProductLine {
    return { isExisting: false, name: '', wholesalePrice: 0, profitPercent: null, profitAmount: null, price: 0,
             compareAtPrice: null, pricingTagId: null, description: '', sizeQuantities: {}, imageFile: null,
             imagePreview: null, imageUrl: '', existingGroup: null, existingSearch: '', restockQuantities: {},
             restockCost: null, existingResults: [] };
  }

  trackByGroupKey(_: number, g: ProductGroup): string { return `${g.baseName}::${g.categoryId}`; }

  addLine() { this.lines.push(this.newLine()); }
  removeLine(i: number) { if (this.lines.length > 1) this.lines.splice(i, 1); }

  onCategoryChange() {
    const cat = this.categories.find(c => Number(c.id) === Number(this.categoryId)) || null;
    this.availableSizes = cat?.sizes ? cat.sizes.split(',').map(s => s.trim()).filter(Boolean) : [];
    // Reset size maps so stale sizes from a previous category don't linger.
    this.lines.forEach(l => {
      l.sizeQuantities = {};
      if (l.isExisting) { l.existingGroup = null; this.refreshExistingResults(l); }
    });
  }

  applyPricingTag(line: ProductLine) {
    const tag = this.pricingTags.find(t => t.id === line.pricingTagId);
    if (!tag || !line.wholesalePrice) return;
    line.price = Math.round(line.wholesalePrice * (1 + tag.profitPercent / 100));
  }

  onImageSelected(event: any, line: ProductLine) {
    const file = event.target.files[0];
    if (!file) return;
    line.imageFile = file;
    const reader = new FileReader();
    reader.onload = () => line.imagePreview = reader.result;
    reader.readAsDataURL(file);
  }

  lineQty(line: ProductLine): number {
    return this.availableSizes.reduce((s, sz) => s + (line.sizeQuantities[sz] || 0), 0);
  }

onSubmit() {
    const newLines = this.lines.filter(l => !l.isExisting && l.name.trim());
    const restockLines = this.lines.filter(l => l.isExisting && l.existingGroup && this.restockLineTotal(l) > 0);

    if (newLines.length === 0 && restockLines.length === 0) {
      this.notify.notify({ type: 'warning', title: 'Nothing to save', message: 'Add a new product name, or pick an existing product and enter a restock quantity.', ttlMs: 4500 });
      return;
    }
    if (newLines.length > 0 && (!this.categoryId || !this.supplierId)) {
      this.notify.notify({ type: 'warning', title: 'Missing fields', message: 'Select a supplier and category first.', ttlMs: 4000 });
      return;
    }

    this.isSubmitting = true;
    this.submitRestocks(restockLines).subscribe({
      next: () => {
        if (newLines.length === 0) { this.finishSubmit(0, restockLines.length); return; }
        this.uploadThenCreate(newLines, restockLines.length);
      },
      error: (err: any) => {
        this.isSubmitting = false;
        this.notify.notify({ type: 'error', title: 'Restock failed', message: err?.error?.message || 'Could not restock one of the products.', ttlMs: 5000 });
      }
    });
  }

  // Restocking an existing product adds stock via Inventory (one purchase call
  // per size/variant) instead of creating a new product row — no duplicates.
  private submitRestocks(restockLines: ProductLine[]): Observable<any> {
    const calls = restockLines.flatMap(l => {
      const group = l.existingGroup!;
      return group.variants
        .filter(v => (l.restockQuantities[v.size || ''] || 0) > 0)
        .map(v => this.inventoryService.purchase(v.id, l.restockQuantities[v.size || ''], l.restockCost ?? undefined,
          this.suppliers.find(s => s.id === this.supplierId)?.name));
    });
    return calls.length ? forkJoin(calls) : of(null);
  }

  private uploadThenCreate(newLines: ProductLine[], restockCount: number) {
    const uploads = newLines.map(l => l.imageFile
      ? this.productService.uploadImage(l.imageFile)
      : of({ imageUrl: '' }));

    forkJoin(uploads).subscribe({
      next: (results) => {
        newLines.forEach((l, idx) => { if (results[idx]?.imageUrl) l.imageUrl = results[idx].imageUrl; });
        this.submitBulk(newLines, restockCount);
      },
      error: () => {
        this.isSubmitting = false;
        this.notify.notify({ type: 'error', title: 'Upload failed', message: 'Could not upload one of the images.', ttlMs: 5000 });
      }
    });
  }

  private finishSubmit(createdCount: number, restockCount: number) {
    const parts: string[] = [];
    if (createdCount) parts.push(`${createdCount} new product entr${createdCount === 1 ? 'y' : 'ies'}`);
    if (restockCount) parts.push(`${restockCount} product${restockCount === 1 ? '' : 's'} restocked`);
    this.notify.notify({ type: 'success', title: 'Saved', message: parts.join(' · '), ttlMs: 3500 });
    const basePath = this.router.url.split('/').slice(0, 3).join('/');
    this.router.navigate([basePath, 'products']);
  }

  private submitBulk(valid: ProductLine[], restockCount = 0) {
    const products: BulkProductLineDto[] = valid.map(l => ({
      name: l.name.trim(),
      price: l.price,
      wholesalePrice: l.wholesalePrice,
      description: l.description || '',
      imageUrl: l.imageUrl || '',
      pricingTagId: l.pricingTagId,
      compareAtPrice: l.compareAtPrice,
      sizes: this.availableSizes
        .filter(sz => (l.sizeQuantities[sz] || 0) > 0)
        .map(sz => ({ size: sz, quantity: l.sizeQuantities[sz] }))
    }));

    this.productService.createBulkProducts({ categoryId: this.categoryId, supplierId: this.supplierId, products }).subscribe({
      next: (res) => this.finishSubmit(res.count, restockCount),
      error: (err) => {
        this.isSubmitting = false;
        this.notify.notify({ type: 'error', title: 'Save failed', message: err.error?.message || 'Could not save the products.', ttlMs: 5000 });
      }
    });
  }
}
