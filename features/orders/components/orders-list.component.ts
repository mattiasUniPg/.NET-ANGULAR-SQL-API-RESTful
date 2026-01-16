// features/orders/components/orders-list.component.ts
import { Component, OnInit, inject, DestroyRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Store } from '@ngrx/store';
import { 
  Observable, 
  Subject, 
  combineLatest, 
  debounceTime, 
  distinctUntilChanged,
  startWith,
  switchMap
} from 'rxjs';
import { OrdersActions } from '../store/orders.actions';
import { 
  selectAllOrders, 
  selectOrdersLoading, 
  selectPagination,
  selectFilters 
} from '../store/orders.selectors';

@Component({
  selector: 'app-orders-list',
  standalone: true,
  imports: [CommonModule, /* Material modules */],
  template: `
    <div class="orders-container">
      <!-- Filters -->
      <mat-card class="filters-card">
        <mat-card-content>
          <div class="filters-row">
            <mat-form-field>
              <mat-label>Cerca</mat-label>
              <input matInput 
                     [formControl]="searchControl" 
                     placeholder="Numero ordine, cliente...">
            </mat-form-field>

            <mat-form-field>
              <mat-label>Stato</mat-label>
              <mat-select [formControl]="statusControl" multiple>
                <mat-option *ngFor="let status of orderStatuses" 
                            [value]="status.value">
                  {{ status.label }}
                </mat-option>
              </mat-select>
            </mat-form-field>

            <mat-form-field>
              <mat-label>Data da</mat-label>
              <input matInput 
                     [matDatepicker]="pickerFrom" 
                     [formControl]="fromDateControl">
              <mat-datepicker-toggle matSuffix [for]="pickerFrom" />
              <mat-datepicker #pickerFrom />
            </mat-form-field>

            <button mat-raised-button 
                    color="primary" 
                    (click)="loadOrders()">
              Cerca
            </button>
            
            <button mat-button (click)="resetFilters()">
              Reset
            </button>
          </div>
        </mat-card-content>
      </mat-card>

      <!-- Loading spinner -->
      <mat-progress-bar *ngIf="loading$ | async" 
                        mode="indeterminate" />

      <!-- Orders table -->
      <mat-card class="table-card">
        <table mat-table [dataSource]="orders$ | async">
          
          <ng-container matColumnDef="orderNumber">
            <th mat-header-cell *matHeaderCellDef>Numero Ordine</th>
            <td mat-cell *matCellDef="let order">
              <a [routerLink]="['/orders', order.id]">
                {{ order.orderNumber }}
              </a>
            </td>
          </ng-container>

          <ng-container matColumnDef="orderDate">
            <th mat-header-cell *matHeaderCellDef>Data</th>
            <td mat-cell *matCellDef="let order">
              {{ order.orderDate | date:'dd/MM/yyyy' }}
            </td>
          </ng-container>

          <ng-container matColumnDef="customerName">
            <th mat-header-cell *matHeaderCellDef>Cliente</th>
            <td mat-cell *matCellDef="let order">
              {{ order.customerName }}
            </td>
          </ng-container>

          <ng-container matColumnDef="totalAmount">
            <th mat-header-cell *matHeaderCellDef>Totale</th>
            <td mat-cell *matCellDef="let order">
              {{ order.totalAmount | currency:'EUR' }}
            </td>
          </ng-container>

          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>Stato</th>
            <td mat-cell *matCellDef="let order">
              <mat-chip [class]="'status-' + order.status">
                {{ getStatusLabel(order.status) }}
              </mat-chip>
            </td>
          </ng-container>

          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef>Azioni</th>
            <td mat-cell *matCellDef="let order">
              <button mat-icon-button [matMenuTriggerFor]="menu">
                <mat-icon>more_vert</mat-icon>
              </button>
              <mat-menu #menu="matMenu">
                <button mat-menu-item (click)="viewOrder(order.id)">
                  <mat-icon>visibility</mat-icon>
                  Visualizza
                </button>
                <button mat-menu-item (click)="editOrder(order.id)">
                  <mat-icon>edit</mat-icon>
                  Modifica
                </button>
                <button mat-menu-item (click)="deleteOrder(order.id)">
                  <mat-icon>delete</mat-icon>
                  Elimina
                </button>
              </mat-menu>
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="displayedColumns" />
          <tr mat-row *matRowDef="let row; columns: displayedColumns" />
        </table>

        <!-- Paginator -->
        <mat-paginator 
          [length]="(pagination$ | async)?.totalCount"
          [pageSize]="(pagination$ | async)?.pageSize"
          [pageIndex]="(pagination$ | async)?.currentPage - 1"
          [pageSizeOptions]="[10, 20, 50, 100]"
          (page)="onPageChange($event)">
        </mat-paginator>
      </mat-card>
    </div>
  `
})
export class OrdersListComponent implements OnInit {
  private readonly store = inject(Store);
  private readonly destroyRef = inject(DestroyRef);

  // Observables from store
  orders$ = this.store.select(selectAllOrders);
  loading$ = this.store.select(selectOrdersLoading);
  pagination$ = this.store.select(selectPagination);
  filters$ = this.store.select(selectFilters);

  // Form controls con RxJS
  searchControl = new FormControl('');
  statusControl = new FormControl<OrderStatus[]>([]);
  fromDateControl = new FormControl<Date | null>(null);
  toDateControl = new FormControl<Date | null>(null);

  // Subject per refresh manuale
  private refreshSubject = new Subject<void>();

  displayedColumns = [
    'orderNumber',
    'orderDate',
    'customerName',
    'totalAmount',
    'status',
    'actions'
  ];

  orderStatuses = [
    { value: OrderStatus.Pending, label: 'In attesa' },
    { value: OrderStatus.Confirmed, label: 'Confermato' },
    { value: OrderStatus.Processing, label: 'In lavorazione' },
    { value: OrderStatus.Shipped, label: 'Spedito' },
    { value: OrderStatus.Delivered, label: 'Consegnato' }
  ];

  ngOnInit(): void {
    // Auto-refresh ogni 30 secondi se ci sono cambiamenti
    const autoRefresh$ = timer(0, 30000).pipe(
      switchMap(() => this.checkForUpdates())
    );

    // Combine search and filters con debounce
    combineLatest([
      this.searchControl.valueChanges.pipe(
        startWith(''),
        debounceTime(300),
        distinctUntilChanged()
      ),
      this.statusControl.valueChanges.pipe(startWith([])),
      this.fromDateControl.valueChanges.pipe(startWith(null)),
      this.toDateControl.valueChanges.pipe(startWith(null)),
      this.refreshSubject.pipe(startWith(undefined))
    ])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(([search, statuses, fromDate, toDate]) => {
        const params: OrderQueryParams = {
          search,
          statuses,
          fromDate: fromDate?.toISOString(),
          toDate: toDate?.toISOString(),
          pageNumber: 1,
          pageSize: 20
        };

        this.store.dispatch(OrdersActions.setFilters({ filters: params }));
        this.loadOrders();
      });
  }

  loadOrders(): void {
    this.filters$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(filters => {
        this.store.dispatch(OrdersActions.loadOrders({ params: filters }));
      });
  }

  resetFilters(): void {
    this.searchControl.setValue('');
    this.statusControl.setValue([]);
    this.fromDateControl.setValue(null);
    this.toDateControl.setValue(null);
    this.store.dispatch(OrdersActions.resetFilters());
  }

  onPageChange(event: PageEvent): void {
    this.filters$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(filters => {
        const params = {
          ...filters,
          pageNumber: event.pageIndex + 1,
          pageSize: event.pageSize
        };
        this.store.dispatch(OrdersActions.loadOrders({ params }));
      });
  }

  deleteOrder(id: number): void {
    // Confirm dialog
    this.store.dispatch(OrdersActions.deleteOrder({ id }));
  }

  private checkForUpdates(): Observable<boolean> {
    // Logica per verificare nuovi ordini
    return of(false);
  }

  getStatusLabel(status: string): string {
    return this.orderStatuses.find(s => s.value.toString() === status)?.label || status;
  }
}
/*  Reactive Component con RxJS */
