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

export interface ServerApkFile {
  name: string;
  sizeKb: number;
  url: string;
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

  getServerFiles(): Observable<ServerApkFile[]> {
    return this.http.get<ServerApkFile[]>(`${this.apiUrl}/server-files`);
  }

  upload(file: File, versionName: string, versionCode: number, releaseNotes: string): Observable<AppVersion> {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('versionName', versionName);
    formData.append('versionCode', versionCode.toString());
    formData.append('releaseNotes', releaseNotes || '');
    return this.http.post<AppVersion>(this.apiUrl, formData);
  }

  registerExisting(fileName: string, versionName: string, versionCode: number, releaseNotes: string): Observable<AppVersion> {
    return this.http.post<AppVersion>(`${this.apiUrl}/register`, { fileName, versionName, versionCode, releaseNotes });
  }

  update(id: number, versionName: string, versionCode: number, releaseNotes: string): Observable<AppVersion> {
    return this.http.put<AppVersion>(`${this.apiUrl}/${id}`, { versionName, versionCode, releaseNotes });
  }

  delete(id: number, deleteFile = false): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}?deleteFile=${deleteFile}`);
  }
}
