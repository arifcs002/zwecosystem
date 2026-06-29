import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ProductService } from '../../../services/product/product.service';
import { ImgUrlPipe } from '../../../pipes/img-url.pipe';

@Component({
  selector: 'app-barcode-management',
  standalone: true,
  imports: [CommonModule, FormsModule, ImgUrlPipe],
  templateUrl: './barcode-management.component.html',
  styleUrl: './barcode-management.component.css'
})
export class BarcodeManagementComponent implements OnInit {
  private productService = inject(ProductService);

  products: any[] = [];
  filteredProducts: any[] = [];
  categories: string[] = [];
  selectedCategory = 'ALL';
  searchQuery = '';
  isLoading = false;
  successMsg = '';
  errorMsg = '';
  selectedProducts: Set<string> = new Set();

  ngOnInit() { this.load(); }

  load() {
    this.isLoading = true;
    this.productService.getProducts().subscribe({
      next: (data: any[]) => {
        this.products = data;
        const cats = [...new Set(data.map((p: any) => p.category?.name).filter(Boolean))] as string[];
        this.categories = ['ALL', ...cats];
        this.applyFilter();
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  applyFilter() {
    let result = [...this.products];
    if (this.selectedCategory !== 'ALL') result = result.filter(p => p.category?.name === this.selectedCategory);
    if (this.searchQuery) {
      const q = this.searchQuery.toLowerCase();
      result = result.filter(p => p.name?.toLowerCase().includes(q) || p.sku?.toLowerCase().includes(q) || p.barcode?.toLowerCase().includes(q));
    }
    this.filteredProducts = result;
  }

  toggleSelect(id: string) {
    if (this.selectedProducts.has(id)) this.selectedProducts.delete(id);
    else this.selectedProducts.add(id);
  }

  selectAll() {
    if (this.selectedProducts.size === this.filteredProducts.length) {
      this.selectedProducts.clear();
    } else {
      this.filteredProducts.forEach(p => this.selectedProducts.add(p.id));
    }
  }

  isAllSelected() {
    return this.filteredProducts.length > 0 && this.selectedProducts.size === this.filteredProducts.length;
  }

  printSelected() {
    const toPrint = this.filteredProducts.filter(p => this.selectedProducts.has(p.id));
    if (toPrint.length === 0) { this.errorMsg = 'No products selected.'; return; }
    this.openPrintPreview(toPrint);
  }

  printSingle(product: any) {
    this.openPrintPreview([product]);
  }

  openPrintPreview(products: any[]) {
    const printWindow = window.open('', '_blank');
    if (!printWindow) return;

    const labels = products.map(p => `
      <div class="label">
        <div class="product-name">${p.name?.substring(0, 30) || 'Product'}</div>
        <svg class="barcode-svg" id="bc-${p.id}"></svg>
        <div class="sku">SKU: ${p.sku || ''}</div>
        <div class="price">\u09f3${p.price || 0}</div>
        <div class="barcode-text">${p.barcode || ''}</div>
      </div>
    `).join('');

    printWindow.document.write(`
      <!DOCTYPE html><html><head><title>Barcodes</title>
      <style>
        body { margin: 0; font-family: Arial, sans-serif; }
        .labels { display: flex; flex-wrap: wrap; gap: 8px; padding: 10px; }
        .label { border: 1px solid #333; border-radius: 4px; padding: 8px; width: 200px; text-align: center; page-break-inside: avoid; }
        .product-name { font-size: 11px; font-weight: bold; margin-bottom: 4px; }
        .barcode-svg { width: 180px; height: 60px; }
        .sku { font-size: 9px; color: #666; margin-top: 2px; }
        .price { font-size: 14px; font-weight: bold; margin-top: 4px; }
        .barcode-text { font-size: 9px; letter-spacing: 2px; margin-top: 2px; }
        @media print { body { margin: 0; } }
      </style></head><body>
      <div class="labels">${labels}</div>
      <script>window.onload = function(){ window.print(); }<\/script>
      </body></html>
    `);
    printWindow.document.close();
  }
}
