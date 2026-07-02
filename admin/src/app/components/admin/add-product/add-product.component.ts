import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { SupplierService, Supplier } from '../../../services/supplier/supplier.service';
import { CategoryService, Category } from '../../../services/category/category.service';
import { ProductService, BulkProductLineDto } from '../../../services/product/product.service';
import { PricingTagService, PricingTag } from '../../../services/pricing-tag/pricing-tag.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';
import { SettingsService } from '../../../services/settings/settings.service';
import { buildIndentedList, FlatCategory } from '../../../utils/category-tree.util';
import { toSecretCode, DEFAULT_SECRET_MAP } from '../../../utils/secret-code.util';

// One product entry in the bulk form.
interface ProductLine {
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
}

@Component({
  selector: 'app-add-product',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './add-product.component.html',
  styleUrl: './add-product.component.css'
})
export class AddProductComponent implements OnInit {
  private supplierService = inject(SupplierService);
  private categoryService = inject(CategoryService);
  private productService = inject(ProductService);
  private pricingTagService = inject(PricingTagService);
  private settingsService = inject(SettingsService);
  private router = inject(Router);
  private notify = inject(GlobalNotificationService);

  secretMap = DEFAULT_SECRET_MAP;

  suppliers: Supplier[] = [];
  categories: Category[] = [];
  pricingTags: PricingTag[] = [];

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
      settings: this.settingsService.getSettings()
    }).subscribe({
      next: ({ suppliers, categories, tags, settings }) => {
        this.suppliers = suppliers.sort((a: any, b: any) => a.name.localeCompare(b.name));
        this.categories = categories;
        this.pricingTags = tags.filter(t => t.isActive);
        const map = settings.find(s => s.key === 'secret_price_map')?.value;
        if (map && map.length >= 10) this.secretMap = map;
      },
      error: (err) => console.error(err)
    });
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
    return { name: '', wholesalePrice: 0, profitPercent: null, profitAmount: null, price: 0, compareAtPrice: null,
             pricingTagId: null, description: '', sizeQuantities: {}, imageFile: null, imagePreview: null, imageUrl: '' };
  }

  addLine() { this.lines.push(this.newLine()); }
  removeLine(i: number) { if (this.lines.length > 1) this.lines.splice(i, 1); }

  onCategoryChange() {
    const cat = this.categories.find(c => Number(c.id) === Number(this.categoryId)) || null;
    this.availableSizes = cat?.sizes ? cat.sizes.split(',').map(s => s.trim()).filter(Boolean) : [];
    // Reset size maps so stale sizes from a previous category don't linger.
    this.lines.forEach(l => l.sizeQuantities = {});
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
    if (!this.categoryId || !this.supplierId) {
      this.notify.notify({ type: 'warning', title: 'Missing fields', message: 'Select a supplier and category first.', ttlMs: 4000 });
      return;
    }
    const valid = this.lines.filter(l => l.name.trim());
    if (valid.length === 0) {
      this.notify.notify({ type: 'warning', title: 'No products', message: 'Add at least one product with a name.', ttlMs: 4000 });
      return;
    }

    this.isSubmitting = true;
    // Upload each line's image (if any) first, then submit the whole batch.
    const uploads = valid.map(l => l.imageFile
      ? this.productService.uploadImage(l.imageFile)
      : of({ imageUrl: '' }));

    forkJoin(uploads.length ? uploads : [of({ imageUrl: '' })]).subscribe({
      next: (results) => {
        valid.forEach((l, idx) => { if (results[idx]?.imageUrl) l.imageUrl = results[idx].imageUrl; });
        this.submitBulk(valid);
      },
      error: () => {
        this.isSubmitting = false;
        this.notify.notify({ type: 'error', title: 'Upload failed', message: 'Could not upload one of the images.', ttlMs: 5000 });
      }
    });
  }

  private submitBulk(valid: ProductLine[]) {
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
      next: (res) => {
        this.notify.notify({ type: 'success', title: 'Products created', message: `${res.count} product entr${res.count === 1 ? 'y' : 'ies'} added.`, ttlMs: 3500 });
        const basePath = this.router.url.split('/').slice(0, 3).join('/');
        this.router.navigate([basePath, 'products']);
      },
      error: (err) => {
        this.isSubmitting = false;
        this.notify.notify({ type: 'error', title: 'Save failed', message: err.error?.message || 'Could not save the products.', ttlMs: 5000 });
      }
    });
  }
}
