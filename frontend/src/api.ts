import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { ShipmentDetail, ShipmentSummary } from './assessment';

@Injectable({ providedIn: 'root' })
export class ShipmentsApi {
  private readonly http = inject(HttpClient);
  #token = '';

  connect(token: string) { this.#token = token; }
  disconnect() { this.#token = ''; }

  private options() {
    return { headers: { Authorization: `Bearer ${this.#token}` }, timeout: 8000 };
  }

  list() { return this.http.get<ShipmentSummary[]>('/api/v1/shipments', this.options()); }
  detail(id: string) { return this.http.get<ShipmentDetail>(this.url(id), this.options()); }
  review(id: string, expectedVersion: number, note: string) {
    return this.http.post<ShipmentDetail>(`${this.url(id)}/reviews`,
      { expectedVersion, decision: 'acknowledged', note }, this.options());
  }

  private url(id: string) { return `/api/v1/shipments/${encodeURIComponent(id)}`; }
}
