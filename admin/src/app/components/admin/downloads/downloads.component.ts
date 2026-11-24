import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../services/auth/auth.service';

const bdNow = () => new Date(Date.now() + 6 * 3600_000);
const iso = (d: Date) => d.toISOString().slice(0, 10);

// Download the shop's numbers as spreadsheet (CSV) files that open in Excel / Google Sheets.
@Component({
  selector: 'app-downloads',
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">⬇️ Download Reports</h1>
      <p class="page-subtitle">Spreadsheet files for Excel or Google Sheets (CSV)</p>
    </div>
  </div>

  <div class="alert-error" *ngIf="errorMsg" role="alert">❌ {{ errorMsg }}</div>

  <div class="table-card" style="padding:16px;margin-bottom:16px">
    <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:end">
      <div class="form-group" style="margin:0"><label>From</label><input class="form-input" type="date" [(ngModel)]="from"></div>
      <div class="form-group" style="margin:0"><label>To</label><input class="form-input" type="date" [(ngModel)]="to"></div>
    </div>
    <p style="opacity:.6;font-size:12.5px;margin:8px 0 0">The dates apply to the sales, returns, profit and expenses files.</p>
  </div>

  <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:12px">
    @for (r of reports; track r.key) {
      @if (!r.owner || isOwner) {
        <div class="table-card" style="padding:14px">
          <div style="font-weight:700;margin-bottom:4px">{{ r.icon }} {{ r.title }}</div>
          <div style="opacity:.65;font-size:12.5px;min-height:34px">{{ r.help }}</div>
          <button class="btn-secondary" style="margin-top:10px" (click)="download(r)" [disabled]="busy === r.key">{{ busy === r.key ? 'Preparing…' : 'Download' }}</button>
        </div>
      }
    }
  </div>
</div>`
})
export class DownloadsComponent {
  private http = inject(HttpClient);
  private auth = inject(AuthService);
  isOwner = this.auth.hasRight('PAGE_STORE_CONFIG');
  from = iso(bdNow()).slice(0, 8) + "01";
  to = iso(bdNow());
  busy = ''; errorMsg = '';

  reports = [
    { key: 'sales', icon: '🧾', title: 'Sales', help: 'Every order in the date range, with customer and payment.', ranged: true, owner: false },
    { key: 'returns', icon: '↩️', title: 'Returns & exchanges', help: 'Goods taken back in the date range.', ranged: true, owner: false },
    { key: 'dues', icon: '📒', title: 'Customer dues (baki)', help: 'Who owes you money right now, biggest first.', ranged: false, owner: false },
    { key: 'supplier-dues', icon: '🏭', title: 'Supplier dues', help: 'What you owe each supplier right now.', ranged: false, owner: false },
    { key: 'profit', icon: '📈', title: 'Profit by day', help: 'Revenue, cost of goods, expenses and profit for each day.', ranged: true, owner: true },
    { key: 'expenses', icon: '💸', title: 'Expenses', help: 'Every expense in the date range.', ranged: true, owner: true },
    { key: 'stock', icon: '📦', title: 'Stock and its value', help: 'Quantity in hand with value at cost and selling price.', ranged: false, owner: true },
  ];

  download(r: { key: string; ranged: boolean }) {
    this.errorMsg = ''; this.busy = r.key;
    const params: Record<string, string> = r.ranged ? { from: this.from, to: this.to } : {};
    this.http.get(`${environment.apiUrl}/reports/export/${r.key}`, { params, observe: 'response', responseType: 'blob' }).subscribe({
      next: res => {
        this.busy = '';
        const name = /filename="?([^";]+)"?/i.exec(res.headers.get('content-disposition') || '')?.[1] || `${r.key}.csv`;
        const url = URL.createObjectURL(res.body as Blob);
        const a = document.createElement('a'); a.href = url; a.download = name; a.click();
        setTimeout(() => URL.revokeObjectURL(url), 2000);
      },
      error: async (e: HttpErrorResponse) => {
        this.busy = '';
        let msg = 'Could not prepare this file.';
        try { msg = JSON.parse(await (e.error as Blob).text()).message || msg; } catch { /* keep default */ }
        this.errorMsg = e.status === 403 ? "You don't have permission to download this." : msg;
      }
    });
  }
}
