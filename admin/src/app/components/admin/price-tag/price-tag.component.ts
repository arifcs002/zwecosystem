import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ProductService, Product } from '../../../services/product/product.service';
import { code39Svg } from '../../../utils/barcode-code39.util';

interface TagRow {
  product: Product;
  selected: boolean;
  copies: number;
}

@Component({
  selector: 'app-price-tag',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './price-tag.component.html',
  styleUrl: './price-tag.component.css'
})
export class PriceTagComponent implements OnInit {
  private productService = inject(ProductService);

  rows: TagRow[] = [];
  filteredRows: TagRow[] = [];
  searchQuery = '';
  isLoading = false;
  errorMsg = '';

  ngOnInit() { this.load(); }

  load() {
    this.isLoading = true;
    this.errorMsg = '';
    this.productService.getProducts().subscribe({
      next: (products) => {
        this.rows = products.map(p => ({ product: p, selected: false, copies: 1 }));
        this.applyFilter();
        this.isLoading = false;
      },
      error: () => { this.errorMsg = 'Failed to load products.'; this.isLoading = false; }
    });
  }

  applyFilter() {
    if (!this.searchQuery) { this.filteredRows = [...this.rows]; return; }
    const q = this.searchQuery.toLowerCase();
    this.filteredRows = this.rows.filter(r =>
      r.product.name?.toLowerCase().includes(q)
      || r.product.barcode?.toLowerCase().includes(q)
      || r.product.sku?.toLowerCase().includes(q));
  }

  codeOf(p: Product): string { return p.uniqueCode || p.barcode || p.sku || ''; }

  isAllSelected() { return this.filteredRows.length > 0 && this.filteredRows.every(r => r.selected); }
  toggleSelectAll() {
    const next = !this.isAllSelected();
    this.filteredRows.forEach(r => r.selected = next);
  }

  get selectedCount() { return this.rows.filter(r => r.selected).length; }

  printSelected() {
    const selected = this.rows.filter(r => r.selected && r.copies > 0);
    if (selected.length === 0) { this.errorMsg = 'Select at least one product to print.'; return; }
    this.errorMsg = '';
    this.print(selected);
  }

  private tagHtml(row: TagRow): string {
    const code = this.codeOf(row.product);
    const tag = `
      <div class="tag">
        <div class="t-name">${esc((row.product.name || '').substring(0, 34))}</div>
        <div class="t-bc">${code39Svg(code, { height: 42 })}</div>
        <div class="t-code">${esc(code)}</div>
        <div class="t-price">৳${row.product.price || 0}</div>
      </div>`;
    return tag.repeat(row.copies);
  }

  private print(rows: TagRow[]) {
    const w = window.open('', '_blank');
    if (!w) return;
    const tags = rows.map(r => this.tagHtml(r)).join('');
    w.document.write(`<!DOCTYPE html><html><head><title>Price Tags</title><style>
      * { box-sizing: border-box; }
      body { margin: 0; font-family: Arial, sans-serif; }
      .sheet { display: flex; flex-wrap: wrap; gap: 2mm; padding: 2mm; }
      .tag { width: 50mm; height: 30mm; padding: 1.5mm; border: 1px solid #999; text-align: center; page-break-inside: avoid; overflow: hidden; }
      .t-name { font-size: 11px; font-weight: 700; margin: 1px 0; line-height: 1.1; }
      .t-bc { width: 100%; }
      .t-bc svg { display: block; width: 100%; }
      .t-code { font-size: 9px; font-family: monospace; letter-spacing: 1px; }
      .t-price { font-size: 13px; font-weight: 800; margin-top: 2px; }
      @media print { body { margin: 0; } }
    </style></head><body><div class="sheet">${tags}</div>
    <script>window.onload=function(){window.print();}<\/script></body></html>`);
    w.document.close();
  }
}

function esc(s: string): string {
  return (s || '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
}
