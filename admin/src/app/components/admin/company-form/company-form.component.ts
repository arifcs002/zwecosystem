import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { CompanyService, Company } from '../../../services/company/company.service';
import { RequiredErrorComponent } from '../../../shared/required-error/required-error.component';

@Component({
  selector: 'app-company-form',
  standalone: true,
  imports: [CommonModule, FormsModule, RequiredErrorComponent],
  templateUrl: './company-form.component.html',
  styleUrl: './company-form.component.css'
})
export class CompanyFormComponent implements OnInit {
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private companyService = inject(CompanyService);

  isEditMode = false;
  activeTab: 'basic' | 'address' | 'payment' = 'basic';
  
  currentCompany: Company = this.getEmptyCompany();

  // Hardcoded Bangladesh Location Data for Dropdowns
  divisions = ['Dhaka', 'Chattogram', 'Sylhet', 'Khulna', 'Rajshahi', 'Barishal', 'Rangpur', 'Mymensingh'];
  
  districtsByDivision: { [key: string]: string[] } = {
    'Dhaka': ['Dhaka', 'Gazipur', 'Narayanganj', 'Tangail', 'Faridpur'],
    'Chattogram': ['Chattogram', 'Cox\'s Bazar', 'Cumilla', 'Feni', 'Noakhali'],
    'Sylhet': ['Sylhet', 'Moulvibazar', 'Habiganj', 'Sunamganj'],
  };

  thanasByDistrict: { [key: string]: string[] } = {
    'Dhaka': ['Gulshan', 'Banani', 'Dhanmondi', 'Mirpur', 'Uttara', 'Motijheel'],
    'Gazipur': ['Tongi', 'Sreepur', 'Kaliakair'],
    'Chattogram': ['Pahartali', 'Panchlaish', 'Double Mooring', 'Halishahar'],
    'Sylhet': ['Kotwali', 'South Surma', 'Bimanbandar'],
  };

  availableDistricts: string[] = [];
  availableThanas: string[] = [];

  ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode = true;
      const parsedId = parseInt(id, 10);
      this.companyService.getCompanyById(parsedId).subscribe({
        next: (company) => {
          if (company) {
            this.currentCompany = { ...company };
            this.onDivisionChange();
            this.onDistrictChange();
            this.currentCompany.district = company.district || '';
            this.currentCompany.thana = company.thana || '';
          }
        },
        error: (err) => console.error(err)
      });
    }
  }

  getEmptyCompany(): Company {
    return { 
      id: 0, name: '', subdomain: '', contactEmail: '', 
      companyMobile: '', ownerName: '', ownerMobile: '', 
      division: '', district: '', thana: '', address: '', 
      facebookLink: '', instagramLink: '', 
      bkashNumber: '', nagadNumber: '', bankName: '', bankAccountName: '', 
      logoUrl: '', isActive: true, approvalStatus: 'Pending', createdAt: new Date()
    };
  }

  onLogoSelect(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) {
      const reader = new FileReader();
      reader.onload = () => {
        this.currentCompany.logoUrl = reader.result as string;
      };
      reader.readAsDataURL(file);
    }
  }

  onDivisionChange() {
    const div = this.currentCompany.division;
    this.availableDistricts = (div && this.districtsByDivision[div]) ? this.districtsByDivision[div] : ['General District'];
    this.currentCompany.district = '';
    this.currentCompany.thana = '';
    this.availableThanas = [];
  }

  onDistrictChange() {
    const dist = this.currentCompany.district;
    this.availableThanas = (dist && this.thanasByDistrict[dist]) ? this.thanasByDistrict[dist] : ['General Thana'];
    this.currentCompany.thana = '';
  }

  saveCompany() {
    if (!this.currentCompany.name || !this.currentCompany.subdomain) {
      alert('Name and Subdomain are required');
      return;
    }
    
    if (this.isEditMode) {
      this.companyService.updateCompany(this.currentCompany).subscribe({
        next: () => {
          alert('Company updated successfully!');
          this.router.navigate(['/admin/companies']);
        },
        error: (err) => console.error(err)
      });
    } else {
      const companyToCreate = { ...this.currentCompany };
      companyToCreate.id = 0;

      this.companyService.addCompany(companyToCreate).subscribe({
        next: () => {
          alert('Company created successfully!');
          this.router.navigate(['/admin/companies']);
        },
        error: (err) => {
          console.error(err);
          alert('Failed to create company: ' + (err.error?.message || err.message || 'Unknown error'));
        }
      });
    }
  }

  cancel() {
    this.router.navigate(['/admin/companies']);
  }
}
