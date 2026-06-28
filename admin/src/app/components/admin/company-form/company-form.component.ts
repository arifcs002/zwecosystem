import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { CompanyService, Company } from '../../../services/company/company.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';
import { SearchFilterPipe } from '../../../pipes/search-filter.pipe';
import { ClickOutsideDirective } from '../../../directives/click-outside.directive';
import { DIVISIONS, getDistrictsByDivision, getUpazilasByDistrict } from '../../../data/bangladesh-geo.data';

@Component({
  selector: 'app-company-form',
  standalone: true,
  imports: [CommonModule, FormsModule, SearchFilterPipe, ClickOutsideDirective],
  templateUrl: './company-form.component.html',
  styleUrl: './company-form.component.css'
})
export class CompanyFormComponent implements OnInit {
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private companyService = inject(CompanyService);
  private notify = inject(GlobalNotificationService);

  isEditMode = false;
  saving = false;
  submitted = false;
  errorMsg = '';

  currentCompany: Company = this.getEmptyCompany();

  // Searchable dropdown state
  divOpen = false;   divSearch = '';
  distOpen = false;  distSearch = '';
  thanaOpen = false; thanaSearch = '';
  unionOpen = false; unionSearch = '';

  readonly divisions = DIVISIONS.map(d => d.name);
  availableDistricts: string[] = [];
  availableThanas: string[] = [];

  ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode = true;
      this.companyService.getCompanyById(parseInt(id, 10)).subscribe({
        next: (company) => {
          this.currentCompany = { ...company };
          if (company.division) this.availableDistricts = getDistrictsByDivision(company.division).map(d => d.name);
          if (company.district) this.availableThanas = getUpazilasByDistrict(company.district).map(u => u.name);
        },
        error: () => this.notify.notify({ type: 'error', title: 'Load failed', message: 'Could not load company data.', ttlMs: 5000 })
      });
    }
  }

  getEmptyCompany(): Company {
    return {
      id: 0, name: '', subdomain: '', contactEmail: '', contactPhone: '',
      companyMobile: '', ownerName: '', ownerMobile: '',
      division: '', district: '', thana: '', unionName: '', address: '',
      facebookLink: '', instagramLink: '',
      bkashNumber: '', nagadNumber: '', bankName: '', bankAccountName: '',
      deliveryCharge: 0, logoUrl: '', bannerUrl: '',
      isActive: true, approvalStatus: 'Pending'
    };
  }

  onLogoSelect(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    if (file.size > 2 * 1024 * 1024) {
      this.notify.notify({ type: 'warning', title: 'File too large', message: 'Logo must be under 2MB.', ttlMs: 5000 });
      return;
    }
    const reader = new FileReader();
    reader.onload = () => { this.currentCompany.logoUrl = reader.result as string; };
    reader.readAsDataURL(file);
  }

  selectDivision(div: string) {
    this.currentCompany.division = div;
    this.availableDistricts = div ? getDistrictsByDivision(div).map(d => d.name) : [];
    this.currentCompany.district = '';
    this.currentCompany.thana = '';
    this.availableThanas = [];
    this.divOpen = false; this.divSearch = '';
  }

  selectDistrict(dist: string) {
    this.currentCompany.district = dist;
    this.availableThanas = dist ? getUpazilasByDistrict(dist).map(u => u.name) : [];
    this.currentCompany.thana = '';
    this.currentCompany.unionName = '';
    this.distOpen = false; this.distSearch = '';
  }

  saveCompany() {
    this.submitted = true;
    this.errorMsg = '';

    const isValid = this.currentCompany.name?.trim() &&
                    this.currentCompany.subdomain?.trim() &&
                    this.currentCompany.ownerName?.trim() &&
                    this.currentCompany.ownerMobile?.trim() &&
                    this.currentCompany.division?.trim() &&
                    this.currentCompany.district?.trim() &&
                    this.currentCompany.thana?.trim();

    if (!isValid) {
      this.errorMsg = 'Please fill in all required fields marked with *';
      return;
    }

    this.saving = true;
    const action$ = this.isEditMode
      ? this.companyService.updateCompany(this.currentCompany)
      : this.companyService.addCompany({ ...this.currentCompany, id: 0 });

    action$.subscribe({
      next: () => {
        this.notify.notify({ type: 'success', title: this.isEditMode ? 'Updated' : 'Created', message: `Company "${this.currentCompany.name}" saved successfully.`, ttlMs: 4000 });
        this.saving = false;
        this.router.navigate(['/admin/companies']);
      },
      error: (err) => {
        this.saving = false;
        this.errorMsg = err.error?.message || err.error?.detail || err.message || 'Failed to save company. Please try again.';
      }
    });
  }

  sanitizeSubdomain(event: Event) {
    const val = (event.target as HTMLInputElement).value;
    this.currentCompany.subdomain = val.toLowerCase().replace(/[^a-z0-9-]/g, '');
  }

  cancel() { this.router.navigate(['/admin/companies']); }
}
