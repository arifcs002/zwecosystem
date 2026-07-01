import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AppVersion {
  id: number;
  versionName: string;
  versionCode: number;
  apkUrl: string;
  releaseNotes?: string;
  isActive: boolean;
  createdDate: string;
}

@Injectable({
  providedIn: 'root'
})
export class AppVersionService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/appversions`;

  getLatest(): Observable<AppVersion> {
    return this.http.get<AppVersion>(`${this.apiUrl}/latest`);
  }

  getAll(): Observable<AppVersion[]> {
    return this.http.get<AppVersion[]>(this.apiUrl);
  }

  upload(file: File, versionName: string, versionCode: number, releaseNotes: string): Observable<AppVersion> {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('versionName', versionName);
    formData.append('versionCode', versionCode.toString());
    formData.append('releaseNotes', releaseNotes || '');
    return this.http.post<AppVersion>(this.apiUrl, formData);
  }
}
