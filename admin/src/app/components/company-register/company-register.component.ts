import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Router, RouterModule } from '@angular/router';

@Component({
  selector: 'app-company-register',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './company-register.component.html'
})
export class CompanyRegisterComponent {
  private http = inject(HttpClient);
  private router = inject(Router);

  formData = {
    companyName: '',
    subdomain: '',
    ownerFirstName: '',
    ownerLastName: '',
    ownerEmail: '',
    ownerPhone: '',
    password: '',
    address: '',
    division: '',
    district: '',
    thana: ''
  };

  loading = false;
  successMessage = '';
  errorMessage = '';

  onSubmit() {
    this.loading = true;
    this.errorMessage = '';
    this.successMessage = '';

    this.http.post('http://localhost:5279/api/auth/register-company', this.formData).subscribe({
      next: (res: any) => {
        this.loading = false;
        this.successMessage = res.message || 'Registration successful. Awaiting Super Admin approval.';
      },
      error: (err) => {
        this.loading = false;
        this.errorMessage = err.error?.message || 'Registration failed. Please try again.';
      }
    });
  }

  goBack() {
    this.router.navigate(['/']);
  }
}
