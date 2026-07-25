import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface SubscriptionPlan {
  id: number;
  name: string;
  price: number;
  billingCycle: string;
  features?: string; // JSON string
}

@Injectable({ providedIn: 'root' })
export class SubscriptionPlanService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/subscriptionplans`;

  getAll(): Observable<SubscriptionPlan[]> {
    return this.http.get<SubscriptionPlan[]>(this.apiUrl);
  }

  create(dto: Partial<SubscriptionPlan>): Observable<SubscriptionPlan> {
    return this.http.post<SubscriptionPlan>(this.apiUrl, dto);
  }

  update(id: number, dto: Partial<SubscriptionPlan>): Observable<SubscriptionPlan> {
    return this.http.put<SubscriptionPlan>(`${this.apiUrl}/${id}`, dto);
  }

  delete(id: number): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/${id}`);
  }

  assignToCompany(companyId: number, subscriptionPlanId: number): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/assign/${companyId}`, { subscriptionPlanId });
  }

  getMyPlan(): Observable<{ plan: SubscriptionPlan | null; usage: { products: number; users: number; orders: number } }> {
    return this.http.get<any>(`${this.apiUrl}/my-plan`);
  }
}
