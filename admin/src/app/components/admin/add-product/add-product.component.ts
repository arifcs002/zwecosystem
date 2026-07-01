import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { SupplierService, Supplier } from '../../../services/supplier/supplier.service';
import { CategoryService, Category } from '../../../services/category/category.service';
import { ProductService, BatchProductCreateDto, SizeQtyDto } from '../../../services/product/product.service';
import { PricingTagService, PricingTag } from '../../../services/pricing-tag/pricing-tag.service';
import { RequiredErrorComponent } from '../../../shared/required-error/required-error.component';

@Component({
  selector: 'app-add-product',
  standalone: true,
  imports: [CommonModule, FormsModule, RequiredErrorComponent],
  templateUrl: './add-product.component.html',
  styleUrl: './add-product.component.css'
})
export class AddProductComponent implements OnInit {
  private supplierService = inject(SupplierService);
  private categoryService = inject(CategoryService);
  private productService = inject(ProductService);
  private pricingTagService = inject(PricingTagService);
  private router = inject(Router);

  suppliers: Supplier[] = [];
  categories: Category[] = [];
  pricingTags: PricingTag[] = [];

  formData: BatchProductCreateDto = {
    supplierId: undefined,
    categoryId: undefined,
    name: '',
    price: 0,
    wholesalePrice: 0,
    description: '',
    imageUrl: '',
    sizes: [],
    pricingTagId: null
  };

  selectedCategoryObj: Category | null = null;
  availableSizes: string[] = [];
  sizeQuantities: { [key: string]: number } = {};

  get totalQty(): number { return this.availableSizes.reduce((s, sz) => s + (this.sizeQuantities[sz] || 0), 0); }
  get selectedSizeCount(): number { return this.availableSizes.filter(sz => (this.sizeQuantities[sz] || 0) > 0).length; }

  isSubmitting: boolean = false;
  imageFile: File | null = null;
  imagePreview: string | ArrayBuffer | null = null;

  ngOnInit() {
    this.loadDropdowns();
  }

  loadDropdowns() {
    this.supplierService.getSuppliers().subscribe({
      next: (data) => this.suppliers = data.sort((a: any, b: any) => a.name.localeCompare(b.name)),
      error: (err) => console.error(err)
    });

    this.categoryService.getCategories().subscribe({
      next: (data) => this.categories = data.sort((a: any, b: any) => a.name.localeCompare(b.name)),
      error: (err) => console.error(err)
    });

    this.pricingTagService.getPricingTags().subscribe({
      next: (data) => this.pricingTags = data.filter(t => t.isActive),
      error: (err) => console.error(err)
    });
  }

  // Auto-calculates sell price from the buy (wholesale) price using the
  // selected tag's profit % — admin can still edit the price afterward.
  applyPricingTag() {
    const tag = this.pricingTags.find(t => t.id === this.formData.pricingTagId);
    if (!tag || !this.formData.wholesalePrice) return;
    this.formData.price = Math.round(this.formData.wholesalePrice * (1 + tag.profitPercent / 100));
  }

  onCategoryChange() {
    const id = Number(this.formData.categoryId);
    this.selectedCategoryObj = this.categories.find(c => Number(c.id) === id) || null;
    this.sizeQuantities = {};
    if (this.selectedCategoryObj?.sizes) {
      this.availableSizes = this.selectedCategoryObj.sizes.split(',').map(s => s.trim()).filter(Boolean);
      this.availableSizes.forEach(s => this.sizeQuantities[s] = 0);
    } else {
      this.availableSizes = [];
    }
  }

  onImageSelected(event: any) {
    const file = event.target.files[0];
    if (file) {
      this.imageFile = file;
      const reader = new FileReader();
      reader.onload = e => this.imagePreview = reader.result;
      reader.readAsDataURL(file);
    }
  }

  onSubmit() {
    if (!this.formData.name || !this.formData.categoryId || !this.formData.supplierId) {
      alert('Please fill out required fields.');
      return;
    }

    // Build sizes array
    this.formData.sizes = [];
    for (const size of this.availableSizes) {
      if (this.sizeQuantities[size] > 0) {
        this.formData.sizes.push({ size, quantity: this.sizeQuantities[size] });
      }
    }

    if (this.formData.sizes.length === 0) {
      alert('Please add quantity for at least one size.');
      return;
    }

    this.isSubmitting = true;

    if (this.imageFile) {
      this.productService.uploadImage(this.imageFile).subscribe({
        next: (res) => {
          this.formData.imageUrl = res.imageUrl;
          this.saveProductBatch();
        },
        error: (err) => {
          console.error(err);
          alert('Image upload failed');
          this.isSubmitting = false;
        }
      });
    } else {
      this.saveProductBatch();
    }
  }

  private saveProductBatch() {
    this.productService.createProductsBatch(this.formData).subscribe({
      next: () => {
        alert('Products successfully created for all specified sizes.');
        const basePath = this.router.url.split('/').slice(0, 3).join('/');
        this.router.navigate([basePath, 'products']);
      },
      error: (err) => {
        console.error(err);
        alert(err.error?.message || 'Failed to save products');
        this.isSubmitting = false;
      }
    });
  }
}
