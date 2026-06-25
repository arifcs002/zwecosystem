import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface CompanySetting {
  companyId?: string;
  key: string;
  value: string;
  groupName: string;
}

@Injectable({
  providedIn: 'root'
})
export class SettingsService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/settings`;

  getSettings(): Observable<CompanySetting[]> {
    return this.http.get<CompanySetting[]>(this.apiUrl);
  }

  updateSettings(settings: CompanySetting[]): Observable<any> {
    return this.http.put<any>(this.apiUrl, settings);
  }
}
