import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { ProductService, Product } from '../../../services/product/product.service';
import { ImgUrlPipe } from '../../../pipes/img-url.pipe';
import { ConfirmDialogService } from '../../../services/confirm-dialog/confirm-dialog.service';

export interface ProductGroup {
  baseName: string;
  imageUrl: string;
  category: string;
  price: number;
  wholesalePrice: number;
  variants: Product[];  // one per size
}

@Component({
  selector: 'app-product-management',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, ImgUrlPipe],
  templateUrl: './product-management.component.html',
  styleUrl: './product-management.component.css'
})
export class ProductManagementComponent implements OnInit {
  private productService = inject(ProductService);
  private confirmSvc = inject(ConfirmDialogService);

  allProducts: Product[] = [];
  productGroups: ProductGroup[] = [];
  filteredGroups: ProductGroup[] = [];
  searchQuery = '';
  isLoading = false;

  // Detail popup
  detailGroup: ProductGroup | null = null;
  stockInputs: { [id: number]: number } = {};

  ngOnInit() { this.loadProducts(); }

  loadProducts() {
    this.isLoading = true;
    this.productService.getProducts().subscribe({
      next: (data) => {
        this.allProducts = data;
        this.buildGroups(data);
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  buildGroups(products: Product[]) {
    const map = new Map<string, Product[]>();
    products.forEach(p => {
      // Strip " (Size X)" suffix to get base name
      const base = p.name.replace(/\s*\(Size [^)]+\)\s*$/i, '').trim();
      if (!map.has(base)) map.set(base, []);
      map.get(base)!.push(p);
    });

    this.productGroups = Array.from(map.entries()).map(([baseName, variants]) => ({
      baseName,
      imageUrl: variants.find(v => v.imageUrl)?.imageUrl || '',
      category: variants[0].category?.name || 'Uncategorized',
      price: variants[0].price,
      wholesalePrice: variants[0].wholesalePrice,
      variants: variants.sort((a, b) => (a.size || '').localeCompare(b.size || '', undefined, { numeric: true }))
    }));

    this.applySearch();
  }

  applySearch() {
    const q = this.searchQuery.toLowerCase();
    this.filteredGroups = q
      ? this.productGroups.filter(g =>
          g.baseName.toLowerCase().includes(q) ||
          g.category.toLowerCase().includes(q)
        )
      : [...this.productGroups];
  }

  get totalStock(): number {
    return this.allProducts.reduce((s, p) => s + p.stockQuantity, 0);
  }

  groupStock(g: ProductGroup): number {
    return g.variants.reduce((s, v) => s + v.stockQuantity, 0);
  }

  detailTotalStock(): number {
    return this.detailGroup ? this.detailGroup.variants.reduce((s, v) => s + v.stockQuantity, 0) : 0;
  }

  // ── Detail Popup ──
  openDetail(group: ProductGroup) {
    this.detailGroup = group;
    this.stockInputs = {};
    group.variants.forEach(v => this.stockInputs[v.id] = 0);
  }

  closeDetail() { this.detailGroup = null; }

  adjustStock(variant: Product, delta: number) {
    const qty = Math.abs(delta > 0 ? (this.stockInputs[variant.id] || 1) : (this.stockInputs[variant.id] || 1));
    const actualDelta = delta > 0 ? qty : -qty;
    this.productService.adjustStock(variant.id, actualDelta).subscribe({
      next: (res) => {
        variant.stockQuantity = res.stockQuantity;
        this.stockInputs[variant.id] = 0;
      }
    });
  }

  async deleteVariant(variant: Product) {
    const ok = await this.confirmSvc.confirm({
      title: 'Delete Size',
      message: `Remove "${variant.name}" from inventory? This cannot be undone.`,
      confirmLabel: 'Delete',
      danger: true
    });
    if (!ok) return;
    this.productService.deleteProduct(variant.id).subscribe({
      next: () => {
        if (this.detailGroup) {
          this.detailGroup.variants = this.detailGroup.variants.filter(v => v.id !== variant.id);
          if (this.detailGroup.variants.length === 0) this.closeDetail();
        }
        this.allProducts = this.allProducts.filter(p => p.id !== variant.id);
        this.buildGroups(this.allProducts);
      }
    });
  }

  async deleteGroup(group: ProductGroup) {
    const ok = await this.confirmSvc.confirm({
      title: 'Delete All Sizes',
      message: `Delete all ${group.variants.length} sizes of "${group.baseName}"? This cannot be undone.`,
      confirmLabel: 'Delete All',
      danger: true
    });
    if (!ok) return;
    const ids = group.variants.map(v => v.id);
    let done = 0;
    ids.forEach(id => {
      this.productService.deleteProduct(id).subscribe({
        next: () => {
          done++;
          if (done === ids.length) {
            this.allProducts = this.allProducts.filter(p => !ids.includes(p.id));
            this.buildGroups(this.allProducts);
          }
        }
      });
    });
  }
}
