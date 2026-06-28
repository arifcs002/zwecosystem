import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { CompanyService, Company } from '../../../services/company/company.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';
import { SearchFilterPipe } from '../../../pipes/search-filter.pipe';
import { ClickOutsideDirective } from '../../../directives/click-outside.directive';

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

  currentCompany: Company = this.getEmptyCompany();

  // Searchable dropdown state
  divOpen = false;   divSearch = '';
  distOpen = false;  distSearch = '';
  thanaOpen = false; thanaSearch = '';

  divisions = ['Dhaka', 'Chattogram', 'Sylhet', 'Khulna', 'Rajshahi', 'Barishal', 'Rangpur', 'Mymensingh'];

  districtsByDivision: { [key: string]: string[] } = {
    'Dhaka': ['Dhaka', 'Gazipur', 'Narayanganj', 'Tangail', 'Faridpur', 'Manikganj', 'Munshiganj', 'Narsingdi'],
    'Chattogram': ['Chattogram', "Cox's Bazar", 'Cumilla', 'Feni', 'Noakhali', 'Lakshmipur', 'Chandpur', 'Brahmanbaria'],
    'Sylhet': ['Sylhet', 'Moulvibazar', 'Habiganj', 'Sunamganj'],
    'Khulna': ['Khulna', 'Jessore', 'Satkhira', 'Bagerhat', 'Narail'],
    'Rajshahi': ['Rajshahi', 'Natore', 'Bogura', 'Naogaon', 'Chapainawabganj'],
    'Barishal': ['Barishal', 'Bhola', 'Patuakhali', 'Pirojpur', 'Jhalokati'],
    'Rangpur': ['Rangpur', 'Dinajpur', 'Gaibandha', 'Kurigram', 'Nilphamari'],
    'Mymensingh': ['Mymensingh', 'Jamalpur', 'Netrokona', 'Sherpur'],
  };

  thanasByDistrict: { [key: string]: string[] } = {
    'Dhaka': ['Gulshan', 'Banani', 'Dhanmondi', 'Mirpur', 'Uttara', 'Motijheel', 'Tejgaon', 'Mohammadpur', 'Khilgaon', 'Badda'],
    'Gazipur': ['Tongi', 'Sreepur', 'Kaliakair', 'Kapasia', 'Gazipur Sadar'],
    'Narayanganj': ['Narayanganj Sadar', 'Fatullah', 'Siddhirganj', 'Sonargaon'],
    'Chattogram': ['Pahartali', 'Panchlaish', 'Double Mooring', 'Halishahar', 'Kotwali', 'Bayazid'],
    "Cox's Bazar": ['Cox Bazar Sadar', 'Ukhiya', 'Teknaf', 'Chakaria'],
    'Sylhet': ['Kotwali', 'South Surma', 'Bimanbandar', 'Osmani Nagar'],
    'Khulna': ['Khulna Sadar', 'Sonadanga', 'Khan Jahan Ali', 'Daulatpur'],
    'Rajshahi': ['Rajshahi Sadar', 'Boalia', 'Motihar', 'Shah Makhdum'],
    'Rangpur': ['Rangpur Sadar', 'Taragonj', 'Badarganj', 'Gangachara'],
  };

  availableDistricts: string[] = [];
  availableThanas: string[] = [];

  ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode = true;
      this.companyService.getCompanyById(parseInt(id, 10)).subscribe({
        next: (company) => {
          this.currentCompany = { ...company };
          if (company.division) {
            this.availableDistricts = this.districtsByDivision[company.division] ?? [];
          }
          if (company.district) {
            this.availableThanas = this.thanasByDistrict[company.district] ?? [];
          }
        },
        error: () => this.notify.notify({ type: 'error', title: 'Load failed', message: 'Could not load company data.', ttlMs: 5000 })
      });
    }
  }

  getEmptyCompany(): Company {
    return {
      id: 0, name: '', subdomain: '', contactEmail: '', contactPhone: '',
      companyMobile: '', ownerName: '', ownerMobile: '',
      division: '', district: '', thana: '', address: '',
      facebookLink: '', instagramLink: '',
      bkashNumber: '', nagadNumber: '', bankName: '', bankAccountName: '',
      deliveryCharge: 0,
      logoUrl: '', bannerUrl: '',
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
    this.availableDistricts = div ? (this.districtsByDivision[div] ?? []) : [];
    this.currentCompany.district = '';
    this.currentCompany.thana = '';
    this.availableThanas = [];
    this.divOpen = false;
    this.divSearch = '';
  }

  selectDistrict(dist: string) {
    this.currentCompany.district = dist;
    this.availableThanas = dist ? (this.thanasByDistrict[dist] ?? []) : [];
    this.currentCompany.thana = '';
    this.distOpen = false;
    this.distSearch = '';
  }

  saveCompany() {
    if (!this.currentCompany.name?.trim() || !this.currentCompany.subdomain?.trim()) {
      this.notify.notify({ type: 'warning', title: 'Validation', message: 'Company Name and Subdomain are required.', ttlMs: 4000 });
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
      error: () => { this.saving = false; }
    });
  }

  cancel() { this.router.navigate(['/admin/companies']); }
}
