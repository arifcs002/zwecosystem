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
  submitted = false;
  errorMsg = '';

  currentCompany: Company = this.getEmptyCompany();

  // Searchable dropdown state
  divOpen = false;   divSearch = '';
  distOpen = false;  distSearch = '';
  thanaOpen = false; thanaSearch = '';

  divisions = [
    'Dhaka', 'Chattogram', 'Sylhet', 'Khulna',
    'Rajshahi', 'Barishal', 'Rangpur', 'Mymensingh'
  ];

  districtsByDivision: { [key: string]: string[] } = {
    'Dhaka': ['Dhaka', 'Gazipur', 'Narayanganj', 'Tangail', 'Faridpur', 'Manikganj', 'Munshiganj', 'Narsingdi', 'Kishoreganj', 'Rajbari', 'Gopalganj', 'Madaripur', 'Shariatpur'],
    'Chattogram': ['Chattogram', "Cox's Bazar", 'Cumilla', 'Feni', 'Noakhali', 'Lakshmipur', 'Chandpur', 'Brahmanbaria', 'Khagrachhari', 'Rangamati', 'Bandarban'],
    'Sylhet': ['Sylhet', 'Moulvibazar', 'Habiganj', 'Sunamganj'],
    'Khulna': ['Khulna', 'Jessore', 'Satkhira', 'Bagerhat', 'Narail', 'Chuadanga', 'Meherpur', 'Magura', 'Kushtia', 'Jhenaidah'],
    'Rajshahi': ['Rajshahi', 'Natore', 'Bogura', 'Naogaon', 'Chapainawabganj', 'Sirajganj', 'Pabna', 'Joypurhat'],
    'Barishal': ['Barishal', 'Bhola', 'Patuakhali', 'Pirojpur', 'Jhalokati', 'Barguna'],
    'Rangpur': ['Rangpur', 'Dinajpur', 'Gaibandha', 'Kurigram', 'Nilphamari', 'Lalmonirhat', 'Thakurgaon', 'Panchagarh'],
    'Mymensingh': ['Mymensingh', 'Jamalpur', 'Netrokona', 'Sherpur'],
  };

  thanasByDistrict: { [key: string]: string[] } = {
    'Dhaka': ['Adabor', 'Airport', 'Badda', 'Banani', 'Bangshal', 'Cantonment', 'Chalkbazar', 'Dhanmondi', 'Demra', 'Gendaria', 'Gulshan', 'Hazaribagh', 'Jatrabari', 'Kafrul', 'Kadamtali', 'Kalabagan', 'Khilgaon', 'Khilkhet', 'Kotwali', 'Lalbagh', 'Mirpur', 'Mohammadpur', 'Motijheel', 'Mugda', 'New Market', 'Pallabi', 'Paltan', 'Rampura', 'Sabujbagh', 'Shah Ali', 'Shahjahanpur', 'Sher-e-Bangla Nagar', 'Shyampur', 'Sutrapur', 'Tejgaon', 'Turag', 'Uttara', 'Vatara', 'Wari'],
    'Gazipur': ['Gazipur Sadar', 'Kaliakair', 'Kapasia', 'Kaliganj', 'Sreepur', 'Tongi'],
    'Narayanganj': ['Araihazar', 'Bandar', 'Narayanganj Sadar', 'Rupganj', 'Siddhirganj', 'Sonargaon', 'Fatullah'],
    'Tangail': ['Tangail Sadar', 'Basail', 'Bhuapur', 'Delduar', 'Dhanbari', 'Ghatail', 'Gopalpur', 'Kalihati', 'Madhupur', 'Mirzapur', 'Nagarpur', 'Sakhipur'],
    'Faridpur': ['Faridpur Sadar', 'Alfadanga', 'Bhanga', 'Boalmari', 'Charbhadrason', 'Madhukhali', 'Nagarkanda', 'Sadarpur', 'Saltha'],
    'Manikganj': ['Manikganj Sadar', 'Daulatpur', 'Ghior', 'Harirampur', 'Saturia', 'Shivalaya', 'Singair'],
    'Munshiganj': ['Munshiganj Sadar', 'Gazaria', 'Lohajang', 'Shreenagar', 'Sirajdikhan', 'Tongibari'],
    'Narsingdi': ['Narsingdi Sadar', 'Belabo', 'Monohardi', 'Palash', 'Raipura', 'Shibpur'],
    'Kishoreganj': ['Kishoreganj Sadar', 'Austagram', 'Bajitpur', 'Bhairab', 'Hossainpur', 'Itna', 'Karimganj', 'Katiadi', 'Kuliarchar', 'Mithamain', 'Nikli', 'Pakundia', 'Tarail'],
    'Gopalganj': ['Gopalganj Sadar', 'Kashiani', 'Kotalipara', 'Muksudpur', 'Tungipara'],
    'Madaripur': ['Madaripur Sadar', 'Kalkini', 'Rajoir', 'Shibchar'],
    'Shariatpur': ['Shariatpur Sadar', 'Bhedarganj', 'Damudya', 'Gosairhat', 'Naria', 'Zanjira'],
    'Rajbari': ['Rajbari Sadar', 'Baliakandi', 'Goalanda', 'Kalukhali', 'Pangsha'],
    'Chattogram': ['Anwara', 'Bayazid', 'Boalkhali', 'Chandgaon', 'Chandanaish', 'Double Mooring', 'Fatikchhari', 'Halishahar', 'Hathazari', 'Karnaphuli', 'Kotwali', 'Lohagara', 'Mirsharai', 'Pahartali', 'Panchlaish', 'Patenga', 'Patiya', 'Rangunia', 'Raozan', 'Sandwip', 'Satkania', 'Sitakunda'],
    "Cox's Bazar": ["Cox's Bazar Sadar", 'Chakaria', 'Moheshkhali', 'Pekua', 'Kutubdia', 'Ramu', 'Teknaf', 'Ukhiya'],
    'Cumilla': ['Cumilla Sadar', 'Brahmanpara', 'Burichang', 'Chandina', 'Chauddagram', 'Daudkandi', 'Debidwar', 'Homna', 'Laksam', 'Lalmai', 'Meghna', 'Monohorgonj', 'Muradnagar', 'Nangalkot', 'Titas'],
    'Noakhali': ['Noakhali Sadar', 'Begumganj', 'Chatkhil', 'Companiganj', 'Hatiya', 'Senbagh', 'Sonaimuri', 'Subarnachar'],
    'Feni': ['Feni Sadar', 'Chhagalnaiya', 'Daganbhuiyan', 'Fulgazi', 'Parshuram', 'Sonagazi'],
    'Lakshmipur': ['Lakshmipur Sadar', 'Kamalnagar', 'Ramganj', 'Ramgati', 'Roypur'],
    'Chandpur': ['Chandpur Sadar', 'Faridganj', 'Haimchar', 'Hajiganj', 'Kachua', 'Matlab Dakshin', 'Matlab Uttar', 'Shahrasti'],
    'Brahmanbaria': ['Brahmanbaria Sadar', 'Akhaura', 'Ashuganj', 'Bancharampur', 'Bijoynagar', 'Kasba', 'Nasirnagar', 'Nabinagar', 'Sarail'],
    'Sylhet': ['Sylhet Sadar', 'Balaganj', 'Beanibazar', 'Biswanath', 'Companigonj', 'Dakshin Surma', 'Fenchuganj', 'Golapganj', 'Gowainghat', 'Jaintiapur', 'Kanaighat', 'Osmani Nagar', 'South Surma'],
    'Moulvibazar': ['Moulvibazar Sadar', 'Barlekha', 'Juri', 'Kamalganj', 'Kulaura', 'Rajnagar', 'Sreemangal'],
    'Habiganj': ['Habiganj Sadar', 'Ajmiriganj', 'Bahubal', 'Baniachong', 'Chunarughat', 'Lakhai', 'Madhabpur', 'Nabiganj', 'Shayestaganj'],
    'Sunamganj': ['Sunamganj Sadar', 'Bishwamvarpur', 'Chhatak', 'Derai', 'Dharampasha', 'Dowarabazar', 'Jagannathpur', 'Jamalganj', 'South Sunamganj', 'Sulla', 'Tahirpur'],
    'Khulna': ['Khulna Sadar', 'Batiaghata', 'Dacope', 'Daulatpur', 'Dighalia', 'Dumuria', 'Koyra', 'Paikgachha', 'Phultala', 'Rupsa', 'Sonadanga', 'Terokhada'],
    'Jessore': ['Jessore Sadar', 'Abhaynagar', 'Bagherpara', 'Chaugachha', 'Jhikargachha', 'Keshabpur', 'Manirampur', 'Sharsha'],
    'Satkhira': ['Satkhira Sadar', 'Assasuni', 'Debhata', 'Kalaroa', 'Kaliganj', 'Shyamnagar', 'Tala'],
    'Bagerhat': ['Bagerhat Sadar', 'Chitalmari', 'Fakirhat', 'Kachua', 'Mollahat', 'Mongla', 'Morrelganj', 'Rampal', 'Sharankhola'],
    'Narail': ['Narail Sadar', 'Kalia', 'Lohagara'],
    'Kushtia': ['Kushtia Sadar', 'Bheramara', 'Daulatpur', 'Khoksa', 'Kumarkhali', 'Mirpur'],
    'Jhenaidah': ['Jhenaidah Sadar', 'Harinakunda', 'Kaliganj', 'Kotchandpur', 'Maheshpur', 'Shailkupa'],
    'Rajshahi': ['Rajshahi Sadar', 'Bagha', 'Bagmara', 'Boalia', 'Charghat', 'Durgapur', 'Godagari', 'Mohanpur', 'Motihar', 'Paba', 'Puthia', 'Shah Makhdum', 'Tanore'],
    'Natore': ['Natore Sadar', 'Bagatipara', 'Baraigram', 'Gurudaspur', 'Lalpur', 'Singra'],
    'Bogura': ['Bogura Sadar', 'Adamdighi', 'Dhunot', 'Dhupchancia', 'Gabtali', 'Kahaloo', 'Nandigram', 'Sariakandi', 'Shajahanpur', 'Sherpur', 'Shibganj', 'Sonatala'],
    'Naogaon': ['Naogaon Sadar', 'Atrai', 'Badalgachhi', 'Dhamoirhat', 'Mahadebpur', 'Manda', 'Niamatpur', 'Patnitala', 'Porsha', 'Raninagar', 'Sapahar'],
    'Sirajganj': ['Sirajganj Sadar', 'Belkuchi', 'Chauhali', 'Kamarkhanda', 'Kazipur', 'Raiganj', 'Shahjadpur', 'Tarash', 'Ullapara'],
    'Pabna': ['Pabna Sadar', 'Atgharia', 'Bera', 'Bhangura', 'Chatmohar', 'Faridpur', 'Ishwardi', 'Santhia', 'Sujanagar'],
    'Barishal': ['Barishal Sadar', 'Agailjhara', 'Babuganj', 'Bakerganj', 'Banaripara', 'Gaurnadi', 'Hizla', 'Mehendiganj', 'Muladi', 'Uzirpur'],
    'Bhola': ['Bhola Sadar', 'Burhanuddin', 'Char Fasson', 'Daulatkhan', 'Lalmohan', 'Manpura', 'Tazumuddin'],
    'Patuakhali': ['Patuakhali Sadar', 'Bauphal', 'Dashmina', 'Dumki', 'Galachipa', 'Kalapara', 'Mirzaganj', 'Rangabali'],
    'Pirojpur': ['Pirojpur Sadar', 'Bhandaria', 'Kawkhali', 'Mathbaria', 'Nazirpur', 'Nesarabad', 'Zianagar'],
    'Jhalokati': ['Jhalokati Sadar', 'Kanthalia', 'Nalchity', 'Rajapur'],
    'Rangpur': ['Rangpur Sadar', 'Badarganj', 'Gangachara', 'Kaunia', 'Mithapukur', 'Pirgachha', 'Pirganj', 'Taragonj'],
    'Dinajpur': ['Dinajpur Sadar', 'Birampur', 'Birganj', 'Biral', 'Bochaganj', 'Chirirbandar', 'Fulbari', 'Ghoraghat', 'Hakimpur', 'Kaharol', 'Khansama', 'Nawabganj', 'Parbatipur'],
    'Gaibandha': ['Gaibandha Sadar', 'Fulchhari', 'Gobindaganj', 'Palashbari', 'Sadullapur', 'Saghata', 'Sundarganj'],
    'Kurigram': ['Kurigram Sadar', 'Bhurungamari', 'Char Rajibpur', 'Chilmari', 'Nageshwari', 'Phulbari', 'Rajarhat', 'Rowmari', 'Ulipur'],
    'Nilphamari': ['Nilphamari Sadar', 'Dimla', 'Domar', 'Jaldhaka', 'Kishoreganj', 'Saidpur'],
    'Lalmonirhat': ['Lalmonirhat Sadar', 'Aditmari', 'Hatibandha', 'Kaliganj', 'Patgram'],
    'Thakurgaon': ['Thakurgaon Sadar', 'Baliadangi', 'Haripur', 'Pirganj', 'Ranisankail'],
    'Panchagarh': ['Panchagarh Sadar', 'Atwari', 'Boda', 'Debiganj', 'Tetulia'],
    'Mymensingh': ['Mymensingh Sadar', 'Bhaluka', 'Dhobaura', 'Fulbaria', 'Gaffargaon', 'Gauripur', 'Haluaghat', 'Ishwarganj', 'Muktagachha', 'Nandail', 'Phulpur', 'Trishal'],
    'Jamalpur': ['Jamalpur Sadar', 'Bakshiganj', 'Dewanganj', 'Islampur', 'Madarganj', 'Melandaha', 'Sarishabari'],
    'Netrokona': ['Netrokona Sadar', 'Atpara', 'Barhatta', 'Durgapur', 'Kalmakanda', 'Kendua', 'Khaliajuri', 'Madan', 'Mohanganj', 'Purbadhala'],
    'Sherpur': ['Sherpur Sadar', 'Jhenaigati', 'Nalitabari', 'Nakla', 'Sreebardi'],
    'Chapainawabganj': ['Chapainawabganj Sadar', 'Bholahat', 'Gomastapur', 'Nachole', 'Shibganj'],
    'Joypurhat': ['Joypurhat Sadar', 'Akkelpur', 'Kalai', 'Khetlal', 'Panchbibi'],
    'Chuadanga': ['Chuadanga Sadar', 'Alamdanga', 'Damurhuda', 'Jibannagar'],
    'Meherpur': ['Meherpur Sadar', 'Gangni', 'Mujibnagar'],
    'Magura': ['Magura Sadar', 'Mohammadpur', 'Shalikha', 'Sreepur'],
    'Barguna': ['Barguna Sadar', 'Amtali', 'Bamna', 'Betagi', 'Pathorghata', 'Taltali'],
    'Khagrachhari': ['Khagrachhari Sadar', 'Dighinala', 'Guimara', 'Lakshmichhari', 'Mahalchhari', 'Manikchhari', 'Matiranga', 'Panchhari', 'Ramgarh'],
    'Rangamati': ['Rangamati Sadar', 'Bagaichhari', 'Barkal', 'Belaichhari', 'Juraichhari', 'Kaptai', 'Kaukhali', 'Langadu', 'Naniarchar', 'Rajasthali'],
    'Bandarban': ['Bandarban Sadar', 'Alikadam', 'Lama', 'Naikhongchhari', 'Rowangchhari', 'Ruma', 'Thanchi'],
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
          if (company.division) this.availableDistricts = this.districtsByDivision[company.division] ?? [];
          if (company.district) this.availableThanas = this.thanasByDistrict[company.district] ?? [];
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
    this.availableDistricts = div ? (this.districtsByDivision[div] ?? []) : [];
    this.currentCompany.district = '';
    this.currentCompany.thana = '';
    this.availableThanas = [];
    this.divOpen = false; this.divSearch = '';
  }

  selectDistrict(dist: string) {
    this.currentCompany.district = dist;
    this.availableThanas = dist ? (this.thanasByDistrict[dist] ?? []) : [];
    this.currentCompany.thana = '';
    this.distOpen = false; this.distSearch = '';
  }

  saveCompany() {
    this.submitted = true;
    this.errorMsg = '';

    const isValid = this.currentCompany.name?.trim() &&
                    this.currentCompany.subdomain?.trim() &&
                    this.currentCompany.ownerName?.trim() &&
                    this.currentCompany.ownerMobile?.trim();

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
