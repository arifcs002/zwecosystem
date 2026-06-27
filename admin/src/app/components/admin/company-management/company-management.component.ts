import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CompanyService, Company } from '../../../services/company/company.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';

@Component({
  selector: 'app-company-management',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './company-management.component.html',
  styleUrl: './company-management.component.css'
})
export class CompanyManagementComponent implements OnInit {
  private router = inject(Router);
  private companyService = inject(CompanyService);
  private notify = inject(GlobalNotificationService);

  companies: Company[] = [];
  loading = false;
  companyToDelete: Company | null = null;
  deleteLoading = false;

  get activeCount() { return this.companies.filter(c => c.isActive).length; }
  get pendingCount() { return this.companies.filter(c => c.approvalStatus === 'Pending').length; }
  get approvedCount() { return this.companies.filter(c => c.approvalStatus === 'Approved').length; }

  ngOnInit() { this.loadCompanies(); }

  loadCompanies() {
    this.loading = true;
    this.companyService.getCompanies().subscribe({
      next: (data) => { this.companies = data; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }

  navigateToCreate() { this.router.navigate(['/admin/company/create']); }
  navigateToEdit(company: Company) { this.router.navigate(['/admin/company/edit', company.id]); }

  toggleStatus(company: Company) {
    this.companyService.toggleStatus(company.id).subscribe({
      next: (res) => {
        company.isActive = res.isActive;
        this.notify.notify({ type: 'success', title: 'Status updated', message: res.message, ttlMs: 4000 });
      },
      error: () => {}
    });
  }

  confirmDelete(company: Company) { this.companyToDelete = company; }

  deleteCompany() {
    if (!this.companyToDelete) return;
    this.deleteLoading = true;
    this.companyService.deleteCompany(this.companyToDelete.id).subscribe({
      next: (res) => {
        this.companies = this.companies.filter(c => c.id !== this.companyToDelete!.id);
        this.notify.notify({ type: 'success', title: 'Deleted', message: res.message, ttlMs: 4000 });
        this.companyToDelete = null;
        this.deleteLoading = false;
      },
      error: () => { this.deleteLoading = false; }
    });
  }
}
