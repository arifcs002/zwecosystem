import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CompanyService, Company } from '../../../services/company/company.service';

const TENANT_ID_KEY = 'tenant_company_id';
const TENANT_NAME_KEY = 'tenant_company_name';

// Super-admin-only tenant switcher. Picking a company writes X-Tenant-ID
// (localStorage) so every subsequent request is scoped to it; "All Companies"
// clears it so the backend returns the global (all-tenant) view. Changing the
// selection reloads so every page refetches in the new context.
@Component({
  selector: 'app-company-selector',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="cs-wrap" *ngIf="companies.length">
      <span class="cs-icon">🏢</span>
      <select class="cs-select" [(ngModel)]="selectedId" (ngModelChange)="onChange($event)">
        <option [ngValue]="null">All Companies</option>
        <option *ngFor="let c of companies" [ngValue]="c.id">{{ c.name }}</option>
      </select>
    </div>
  `,
  styles: [`
    .cs-wrap { display: inline-flex; align-items: center; gap: 6px; background: #f3f0ff; border: 1px solid #ddd6fe;
      border-radius: 10px; padding: 4px 10px; }
    .cs-icon { font-size: 14px; }
    .cs-select { border: 0; background: transparent; font-size: 13px; font-weight: 600; color: #4c1d95;
      cursor: pointer; outline: none; max-width: 180px; }
  `]
})
export class CompanySelectorComponent implements OnInit {
  private companyService = inject(CompanyService);

  companies: Company[] = [];
  selectedId: number | null = null;

  ngOnInit() {
    const stored = localStorage.getItem(TENANT_ID_KEY);
    this.selectedId = stored ? Number(stored) : null;

    this.companyService.getCompanies().subscribe({
      next: (list) => { this.companies = (list || []).sort((a, b) => a.name.localeCompare(b.name)); },
      error: () => { this.companies = []; }
    });
  }

  onChange(id: number | null) {
    if (id == null) {
      localStorage.removeItem(TENANT_ID_KEY);
      localStorage.removeItem(TENANT_NAME_KEY);
    } else {
      const name = this.companies.find(c => c.id === id)?.name || '';
      localStorage.setItem(TENANT_ID_KEY, String(id));
      localStorage.setItem(TENANT_NAME_KEY, name);
    }
    // Reload so every open page refetches its data in the newly-selected tenant.
    window.location.reload();
  }
}
