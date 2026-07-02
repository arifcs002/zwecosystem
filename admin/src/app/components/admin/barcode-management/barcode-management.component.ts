import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ProductService } from '../../../services/product/product.service';
import { SettingsService } from '../../../services/settings/settings.service';
import { code39Svg } from '../../../utils/barcode-code39.util';
import { toSecretCode, DEFAULT_SECRET_MAP } from '../../../utils/secret-code.util';

type TagFormat = 'thermal' | 'label' | 'a4';

@Component({
  selector: 'app-barcode-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './barcode-management.component.html',
  styleUrl: './barcode-management.component.css'
})
export class BarcodeManagementComponent implements OnInit {
  private productService = inject(ProductService);
  private settingsService = inject(SettingsService);
  private sanitizer = inject(DomSanitizer);

  products: any[] = [];
  filteredProducts: any[] = [];
  categories: string[] = [];
  selectedCategory = 'ALL';
  searchQuery = '';
  isLoading = false;
  selectedProducts = new Set<number>();

  format: TagFormat = 'label';
  showPrice = true;
  companyName = '';
  secretMap = DEFAULT_SECRET_MAP;

  ngOnInit() {
    this.isLoading = true;
    forkJoin({
      products: this.productService.getProducts(),
      settings: this.settingsService.getSettings()
    }).subscribe({
      next: ({ products, settings }) => {
        this.products = products;
        const cats = [...new Set(products.map((p: any) => p.category?.name).filter(Boolean))] as string[];
        this.categories = ['ALL', ...cats];
        this.companyName = settings.find(s => s.key === 'store_name')?.value || '';
        const map = settings.find(s => s.key === 'secret_price_map')?.value;
        if (map && map.length >= 10) this.secretMap = map;
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
      result = result.filter(p => p.name?.toLowerCase().includes(q) || p.sku?.toLowerCase().includes(q)
        || p.barcode?.toLowerCase().includes(q) || p.uniqueCode?.toLowerCase().includes(q));
    }
    this.filteredProducts = result;
  }

  toggleSelect(id: number) {
    if (this.selectedProducts.has(id)) this.selectedProducts.delete(id);
    else this.selectedProducts.add(id);
  }
  selectAll() {
    if (this.isAllSelected()) this.selectedProducts.clear();
    else this.filteredProducts.forEach(p => this.selectedProducts.add(p.id));
  }
  isAllSelected() { return this.filteredProducts.length > 0 && this.filteredProducts.every(p => this.selectedProducts.has(p.id)); }

  codeOf(p: any): string { return p.uniqueCode || p.barcode || ''; }
  secretOf(p: any): string { return toSecretCode(p.price, this.secretMap); }

  // Sanitized barcode SVG for the on-page preview.
  previewBarcode(p: any): SafeHtml {
    return this.sanitizer.bypassSecurityTrustHtml(code39Svg(this.codeOf(p), { height: 40 }));
  }
  get previewProduct(): any | null {
    const sel = this.filteredProducts.filter(p => this.selectedProducts.has(p.id));
    return sel[0] || this.filteredProducts[0] || null;
  }

  // ── Printing ───────────────────────────────────────────────
  printSingle(p: any) { this.print([p]); }
  printSelected() {
    const toPrint = this.filteredProducts.filter(p => this.selectedProducts.has(p.id));
    if (toPrint.length) this.print(toPrint);
  }

  private tagHtml(p: any): string {
    const parts = [p.category?.name, p.size].filter(Boolean).join(' · ');
    return `
      <div class="tag">
        ${this.companyName ? `<div class="t-company">${esc(this.companyName)}</div>` : ''}
        <div class="t-name">${esc((p.name || '').substring(0, 34))}</div>
        ${parts ? `<div class="t-meta">${esc(parts)}</div>` : ''}
        <div class="t-bc">${code39Svg(this.codeOf(p), { height: 42 })}</div>
        <div class="t-code">${esc(this.codeOf(p))}</div>
        <div class="t-bottom">
          ${this.showPrice ? `<span class="t-price">৳${p.price || 0}</span>` : ''}
          <span class="t-secret">${esc(this.secretOf(p))}</span>
        </div>
      </div>`;
  }

  private print(products: any[]) {
    const w = window.open('', '_blank');
    if (!w) return;
    const tags = products.map(p => this.tagHtml(p)).join('');
    w.document.write(`<!DOCTYPE html><html><head><title>Price Tags</title><style>
      * { box-sizing: border-box; }
      body { margin: 0; font-family: Arial, sans-serif; }
      .sheet { display: flex; flex-wrap: wrap; ${this.sheetCss()} }
      .tag { border: 1px solid #999; text-align: center; page-break-inside: avoid; ${this.tagCss()} }
      .t-company { font-size: 9px; font-weight: 700; text-transform: uppercase; letter-spacing: .5px; }
      .t-name { font-size: 11px; font-weight: 700; margin: 1px 0; line-height: 1.1; }
      .t-meta { font-size: 8px; color: #444; }
      .t-bc { width: 100%; }
      .t-bc svg { display: block; width: 100%; }
      .t-code { font-size: 9px; font-family: monospace; letter-spacing: 1px; }
      .t-bottom { display: flex; justify-content: space-between; align-items: center; margin-top: 2px; padding: 0 2px; }
      .t-price { font-size: 13px; font-weight: 800; }
      .t-secret { font-size: 12px; font-weight: 700; font-family: monospace; letter-spacing: 1px; }
      @media print { body { margin: 0; } @page { margin: ${this.format === 'a4' ? '8mm' : '0'}; } }
    </style></head><body><div class="sheet">${tags}</div>
    <script>window.onload=function(){window.print();}<\/script></body></html>`);
    w.document.close();
  }

  private sheetCss(): string {
    if (this.format === 'thermal') return 'width: 48mm; gap: 0; flex-direction: column;';
    if (this.format === 'label') return 'gap: 2mm; padding: 2mm;';
    return 'gap: 3mm; padding: 4mm;'; // a4
  }
  private tagCss(): string {
    if (this.format === 'thermal') return 'width: 48mm; padding: 2mm; border: none; border-bottom: 1px dashed #999;';
    if (this.format === 'label') return 'width: 50mm; height: 30mm; padding: 1.5mm; overflow: hidden;';
    return 'width: 48mm; padding: 2mm;'; // a4 grid
  }
}

function esc(s: string): string {
  return (s || '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
}
