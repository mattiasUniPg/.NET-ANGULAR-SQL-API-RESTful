// core/services/api.service.ts
import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError, timer } from 'rxjs';
import { 
  catchError, 
  retry, 
  retryWhen, 
  mergeMap, 
  finalize,
  shareReplay,
  map
} from 'rxjs/operators';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;
  
  // Cache per richieste frequenti
  private cache = new Map<string, Observable<any>>();

  get<T>(
    endpoint: string, 
    params?: HttpParams, 
    useCache = false
  ): Observable<T> {
    const url = `${this.baseUrl}/${endpoint}`;
    const cacheKey = `${url}?${params?.toString() || ''}`;

    if (useCache && this.cache.has(cacheKey)) {
      return this.cache.get(cacheKey)!;
    }

    const request$ = this.http.get<T>(url, { params }).pipe(
      retryWhen(errors => this.retryStrategy(errors)),
      catchError(this.handleError),
      shareReplay(1) // Cache dell'ultimo valore
    );

    if (useCache) {
      this.cache.set(cacheKey, request$);
      // Auto-cleanup cache dopo 5 minuti
      timer(300000).subscribe(() => this.cache.delete(cacheKey));
    }

    return request$;
  }

  post<T>(endpoint: string, body: any): Observable<T> {
    return this.http.post<T>(`${this.baseUrl}/${endpoint}`, body).pipe(
      catchError(this.handleError)
    );
  }

  put<T>(endpoint: string, body: any): Observable<T> {
    return this.http.put<T>(`${this.baseUrl}/${endpoint}`, body).pipe(
      catchError(this.handleError)
    );
  }

  patch<T>(endpoint: string, body: any): Observable<T> {
    return this.http.patch<T>(`${this.baseUrl}/${endpoint}`, body).pipe(
      catchError(this.handleError)
    );
  }

  delete<T>(endpoint: string): Observable<T> {
    return this.http.delete<T>(`${this.baseUrl}/${endpoint}`).pipe(
      catchError(this.handleError)
    );
  }

  private retryStrategy(errors: Observable<any>): Observable<any> {
    return errors.pipe(
      mergeMap((error, index) => {
        const retryAttempt = index + 1;
        // Retry solo per errori di rete (non 4xx/5xx business logic)
        if (retryAttempt > 3 || error.status >= 400) {
          return throwError(() => error);
        }
        
        console.log(`Retry attempt ${retryAttempt} dopo ${retryAttempt * 1000}ms`);
        return timer(retryAttempt * 1000);
      })
    );
  }

  private handleError(error: HttpErrorResponse): Observable<never> {
    let errorMessage = 'Si è verificato un errore';

    if (error.error instanceof ErrorEvent) {
      // Client-side error
      errorMessage = `Errore: ${error.error.message}`;
    } else {
      // Server-side error
      errorMessage = `Codice: ${error.status}\nMessaggio: ${error.message}`;
      
      if (error.error?.title) {
        errorMessage = error.error.title;
      }
    }

    console.error(errorMessage);
    return throwError(() => new Error(errorMessage));
  }

  clearCache(): void {
    this.cache.clear();
  }
}

// core/services/orders-api.service.ts
@Injectable({ providedIn: 'root' })
export class OrdersApiService {
  private readonly api = inject(ApiService);

  getOrders(params: OrderQueryParams): Observable<PaginatedResponse<Order>> {
    const httpParams = new HttpParams({ fromObject: params as any });
    return this.api.get<PaginatedResponse<Order>>('orders', httpParams);
  }

  getOrderById(id: number): Observable<OrderDetail> {
    return this.api.get<OrderDetail>(`orders/${id}`, undefined, true); // with cache
  }

  createOrder(request: CreateOrderRequest): Observable<OrderDetail> {
    return this.api.post<OrderDetail>('orders', request);
  }

  updateOrderStatus(id: number, status: OrderStatus): Observable<void> {
    return this.api.patch<void>(`orders/${id}/status`, { newStatus: status });
  }

  deleteOrder(id: number): Observable<void> {
    return this.api.delete<void>(`orders/${id}`);
  }

  exportOrders(params: OrderQueryParams): Observable<Blob> {
    const httpParams = new HttpParams({ fromObject: params as any });
    return this.http.get(`${this.api['baseUrl']}/orders/export`, {
      params: httpParams,
      responseType: 'blob'
    });
  }
}
