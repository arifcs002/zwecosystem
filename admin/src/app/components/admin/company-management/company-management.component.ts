import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CompanyService, Company } from '../../../services/company/company.service';

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

  companies: Company[] = [];

  ngOnInit() {
    this.loadCompanies();
  }

  loadCompanies() {
    this.companyService.getCompanies().subscribe({
      next: (data: any) => this.companies = data,
      error: (err: any) => console.error(err)
    });
  }

  navigateToCreate() {
    this.router.navigate(['/admin/company/create']);
  }

  navigateToEdit(company: Company) {
    this.router.navigate(['/admin/company/edit', company.id]);
  }

  toggleStatus(company: Company) {
    if(confirm(`Are you sure you want to change status for ${company.name}?`)) {
      company.isActive = !company.isActive;
      this.companyService.updateCompany(company).subscribe({
        next: () => this.loadCompanies(),
        error: (err: any) => console.error(err)
      });
    }
  }

  deleteCompany(company: Company) {
    if(confirm(`Are you sure you want to delete ${company.name}?`)) {
      this.companyService.deleteCompany(company.id).subscribe({
        next: () => this.loadCompanies(),
        error: (err: any) => console.error(err)
      });
    }
  }
}
