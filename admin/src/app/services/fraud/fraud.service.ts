import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class FraudService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/fraud`;

  getFlagged(): Observable<any[]> {
    return this.http.get<any[]>(`${this.apiUrl}/flagged`);
  }

  resolve(id: number, decision: string): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}/resolve`, { decision });
  }
}
