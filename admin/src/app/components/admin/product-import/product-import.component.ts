import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import { ImportRow, TEMPLATE_CSV, mapTable, parseCsv } from '../../../utils/csv.util';

interface RowReport { row: number; name: string; errors: string[]; warnings: string[]; }
interface ImportReport { message?: string; summary?: { total: number; valid: number; withErrors: number; newCategories: string[] }; rows?: RowReport[]; imported?: number; rowsCreated?: number; code?: string; }

// Load many products at once from a spreadsheet saved as CSV. Nothing is saved until the check passes, and a
// single bad row stops the whole import so the catalogue is never left half-loaded.
@Component({
    selector: 'app-product-import',
    imports: [CommonModule, FormsModule],
    template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">📥 Import Products</h1>
      <p class="page-subtitle">Add many products at once from an Excel / Google Sheets file</p>
    </div>
    <button class="btn-secondary" type="button" (click)="downloadTemplate()">⬇ Download template</button>
  </div>

  <div class="table-card" style="padding:16px;margin-bottom:14px">
    <ol style="margin:0 0 12px 18px;padding:0;font-size:13.5px;line-height:1.7">
      <li>Download the template and fill one product per row (Excel or Google Sheets).</li>
      <li>Save it as <b>CSV (UTF-8)</b> — in Excel: File → Save As → “CSV UTF-8”.</li>
      <li>Choose the file below, check the preview, then import.</li>
    </ol>
    <div style="font-size:12.5px;opacity:.7;margin-bottom:10px">
      Columns: <b>name, category, price, wholesale_price</b> are required. Use <b>quantity</b> for a plain product, or <b>sizes</b> like <code>40:5; 41:3</code> for sizes.
      Optional: barcode (to keep one already printed on your stock), description, compare_at_price, unit.
    </div>
    <input type="file" accept=".csv,text/csv" (change)="onFile($event)" aria-label="Choose a CSV file">
    @if (fileName) {
      <p style="margin:8px 0 0;font-size:13px">{{ fileName }} — {{ rows.length }} row{{ rows.length === 1 ? '' : 's' }}</p>
    }
    @if (problem) {
      <p role="alert" style="color:#f87171;margin:8px 0 0;font-size:13.5px">❌ {{ problem }}</p>
    }
  </div>

  @if (rows.length && !done) {
    <div class="table-card" style="padding:16px">
      <label style="display:flex;gap:8px;align-items:center;font-size:13.5px;margin-bottom:10px">
        <input type="checkbox" [(ngModel)]="createCategories" (ngModelChange)="check()"> Create categories that don't exist yet
      </label>
      <div style="display:flex;gap:10px;flex-wrap:wrap;margin-bottom:10px">
        <button class="btn-secondary" (click)="check()" [disabled]="busy">{{ busy && !importing ? 'Checking…' : 'Check file' }}</button>
        <button class="btn-primary" (click)="runImport()" [disabled]="busy || !canImport">{{ importing ? 'Importing…' : 'Import ' + (report?.summary?.valid || 0) + ' products' }}</button>
      </div>
      @if (report?.summary; as s) {
        <p style="font-size:13.5px;margin:0 0 8px">
          <b [style.color]="s.withErrors ? '#f87171' : '#34d399'">{{ s.withErrors ? s.withErrors + ' row(s) need fixing — nothing will be imported until they are fixed.' : 'All ' + s.total + ' rows look good.' }}</b>
          @if (s.newCategories.length) {
            <span> New categories: {{ s.newCategories.join(', ') }}.</span>
          }
        </p>
      }
      @if (report?.message && !report?.summary) {
        <p style="color:#f87171">{{ report?.message }}</p>
      }
      <div style="overflow-x:auto">
        <table class="data-table" style="width:100%;min-width:0;font-size:13px">
          <thead><tr><th>#</th><th>Name</th><th>Category</th><th style="text-align:right">Price</th><th style="text-align:right">Buy</th><th>Stock</th><th>Check</th></tr></thead>
          <tbody>
            @for (r of rows | slice:0:200; track r; let i = $index) {
              <tr>
                <td>{{ i + 1 }}</td><td>{{ r.name }}</td><td>{{ r.category }}</td>
                <td style="text-align:right">{{ r.price }}</td><td style="text-align:right">{{ r.wholesalePrice }}</td>
                <td>{{ r.sizes || r.quantity }}</td>
                <td>
                  @if (report?.rows?.[i]; as rr) {
                    @for (e of rr.errors; track e) {
                      <span style="display:block;color:#f87171">✕ {{ e }}</span>
                    }
                    @for (w of rr.warnings; track w) {
                      <span style="display:block;color:#fbbf24">⚠ {{ w }}</span>
                    }
                    @if (!rr.errors.length && !rr.warnings.length) {
                      <span style="color:#34d399">✓</span>
                    }
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
        @if (rows.length > 200) {
          <p style="opacity:.6;font-size:12.5px">Showing the first 200 rows; all {{ rows.length }} are checked.</p>
        }
      </div>
    </div>
  }

  @if (done) {
    <div class="table-card" role="status" style="padding:16px;border:1px solid rgba(52,211,153,.4)">
      <h3 style="margin:0 0 6px;color:#34d399">✅ Imported {{ report?.imported }} products</h3>
      <div style="font-size:13.5px">{{ report?.rowsCreated }} items were added to your stock (each size is its own item). Print barcodes from the Barcodes page when you are ready.</div>
      <button class="btn-secondary" style="margin-top:12px" (click)="reset()">Import another file</button>
    </div>
  }
</div>`
})
export class ProductImportComponent {
  private http = inject(HttpClient);
  private api = `${environment.apiUrl}/products/import`;

  fileName = ''; rows: ImportRow[] = [];
  problem = ''; report: ImportReport | null = null;
  createCategories = false; busy = false; importing = false; done = false;

  get canImport() { return !!this.report?.summary && this.report.summary.withErrors === 0 && this.report.summary.valid > 0; }

  downloadTemplate() {
    const url = URL.createObjectURL(new Blob([TEMPLATE_CSV], { type: 'text/csv;charset=utf-8' }));
    const a = document.createElement('a');
    a.href = url; a.download = 'products-template.csv'; a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  async onFile(ev: Event) {
    this.reset(false);
    const file = (ev.target as HTMLInputElement).files?.[0];
    if (!file) return;
    if (file.size > 5 * 1024 * 1024) { this.problem = 'That file is too large (over 5 MB). Split it into smaller files.'; return; }
    if (/\.xlsx?$/i.test(file.name)) { this.problem = 'Please save the sheet as “CSV UTF-8” first (File → Save As), then choose the .csv file.'; return; }
    this.fileName = file.name;
    const mapped = mapTable(parseCsv(await file.text()));
    if (mapped.missingRequired.length) {
      this.problem = `The file is missing these columns: ${mapped.missingRequired.join(', ')}. Use the template for the right headings.`;
      this.rows = []; return;
    }
    if (mapped.rows.length === 0) { this.problem = 'The file has no product rows.'; return; }
    if (mapped.rows.length > 1000) { this.problem = 'A file can have at most 1000 products. Split it into smaller files.'; return; }
    this.rows = mapped.rows;
    this.check();
  }

  check() { if (this.rows.length) this.send(true); }
  runImport() { if (this.canImport) this.send(false); }

  private send(dryRun: boolean) {
    this.busy = true; this.importing = !dryRun; this.problem = '';
    // Rows with an unreadable number are sent as null so the server explains them in plain words.
    const rows = this.rows.map(r => ({ ...r, price: this.num(r.price), wholesalePrice: this.num(r.wholesalePrice), quantity: this.num(r.quantity), compareAtPrice: this.num(r.compareAtPrice) }));
    this.http.post<ImportReport>(this.api, { dryRun, createMissingCategories: this.createCategories, rows }).subscribe({
      next: r => { this.busy = false; this.importing = false; this.report = r; if (!dryRun) this.done = true; },
      error: (e: HttpErrorResponse) => {
        this.busy = false; this.importing = false;
        this.report = e?.error?.rows ? e.error : null;
        this.problem = e?.error?.rows ? '' : (e?.error?.message || 'Could not check the file.');
      }
    });
  }

  private num(v: number | null | undefined) { return v == null || Number.isNaN(v) ? null : v; }

  reset(clearInput = true) {
    this.rows = []; this.report = null; this.problem = ''; this.done = false; this.fileName = ''; this.busy = false; this.importing = false;
    if (clearInput) this.createCategories = false;
  }
}
