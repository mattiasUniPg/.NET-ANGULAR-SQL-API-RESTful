// features/orders/store/orders.state.ts
export interface OrdersState {
  orders: Order[];
  selectedOrder: OrderDetail | null;
  loading: boolean;
  error: string | null;
  pagination: {
    currentPage: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
  };
  filters: OrderQueryParams;
}

export const initialOrdersState: OrdersState = {
  orders: [],
  selectedOrder: null,
  loading: false,
  error: null,
  pagination: {
    currentPage: 1,
    pageSize: 20,
    totalCount: 0,
    totalPages: 0
  },
  filters: {}
};

// features/orders/store/orders.actions.ts
import { createActionGroup, emptyProps, props } from '@ngrx/store';

export const OrdersActions = createActionGroup({
  source: 'Orders',
  events: {
    'Load Orders': props<{ params: OrderQueryParams }>(),
    'Load Orders Success': props<{ response: PaginatedResponse<Order> }>(),
    'Load Orders Failure': props<{ error: string }>(),
    
    'Load Order Details': props<{ id: number }>(),
    'Load Order Details Success': props<{ order: OrderDetail }>(),
    'Load Order Details Failure': props<{ error: string }>(),
    
    'Create Order': props<{ request: CreateOrderRequest }>(),
    'Create Order Success': props<{ order: OrderDetail }>(),
    'Create Order Failure': props<{ error: string }>(),
    
    'Update Order Status': props<{ id: number; status: OrderStatus }>(),
    'Update Order Status Success': props<{ id: number; status: OrderStatus }>(),
    'Update Order Status Failure': props<{ error: string }>(),
    
    'Delete Order': props<{ id: number }>(),
    'Delete Order Success': props<{ id: number }>(),
    'Delete Order Failure': props<{ error: string }>(),
    
    'Set Filters': props<{ filters: OrderQueryParams }>(),
    'Reset Filters': emptyProps(),
    'Clear Error': emptyProps()
  }
});

// features/orders/store/orders.reducer.ts
import { createReducer, on } from '@ngrx/store';
import { OrdersActions } from './orders.actions';

export const ordersReducer = createReducer(
  initialOrdersState,
  
  // Load orders
  on(OrdersActions.loadOrders, (state, { params }) => ({
    ...state,
    loading: true,
    error: null,
    filters: params
  })),
  
  on(OrdersActions.loadOrdersSuccess, (state, { response }) => ({
    ...state,
    orders: response.data,
    pagination: {
      currentPage: response.currentPage,
      pageSize: response.pageSize,
      totalCount: response.totalCount,
      totalPages: response.totalPages
    },
    loading: false
  })),
  
  on(OrdersActions.loadOrdersFailure, (state, { error }) => ({
    ...state,
    loading: false,
    error
  })),
  
  // Load order details
  on(OrdersActions.loadOrderDetails, (state) => ({
    ...state,
    loading: true,
    error: null
  })),
  
  on(OrdersActions.loadOrderDetailsSuccess, (state, { order }) => ({
    ...state,
    selectedOrder: order,
    loading: false
  })),
  
  // Create order
  on(OrdersActions.createOrderSuccess, (state, { order }) => ({
    ...state,
    orders: [order, ...state.orders],
    selectedOrder: order,
    loading: false
  })),
  
  // Update status
  on(OrdersActions.updateOrderStatusSuccess, (state, { id, status }) => ({
    ...state,
    orders: state.orders.map(o => 
      o.id === id ? { ...o, status: status.toString() } : o
    ),
    selectedOrder: state.selectedOrder?.id === id 
      ? { ...state.selectedOrder, status: status.toString() }
      : state.selectedOrder,
    loading: false
  })),
  
  // Delete order
  on(OrdersActions.deleteOrderSuccess, (state, { id }) => ({
    ...state,
    orders: state.orders.filter(o => o.id !== id),
    selectedOrder: state.selectedOrder?.id === id ? null : state.selectedOrder,
    loading: false
  })),
  
  // Filters
  on(OrdersActions.setFilters, (state, { filters }) => ({
    ...state,
    filters
  })),
  
  on(OrdersActions.resetFilters, (state) => ({
    ...state,
    filters: {}
  }))
);

// features/orders/store/orders.selectors.ts
import { createFeatureSelector, createSelector } from '@ngrx/store';

export const selectOrdersState = createFeatureSelector<OrdersState>('orders');

export const selectAllOrders = createSelector(
  selectOrdersState,
  (state) => state.orders
);

export const selectSelectedOrder = createSelector(
  selectOrdersState,
  (state) => state.selectedOrder
);

export const selectOrdersLoading = createSelector(
  selectOrdersState,
  (state) => state.loading
);

export const selectOrdersError = createSelector(
  selectOrdersState,
  (state) => state.error
);

export const selectPagination = createSelector(
  selectOrdersState,
  (state) => state.pagination
);

export const selectFilters = createSelector(
  selectOrdersState,
  (state) => state.filters
);

// Selector computed avanzato
export const selectOrdersByStatus = (status: OrderStatus) => createSelector(
  selectAllOrders,
  (orders) => orders.filter(o => o.status === status.toString())
);

export const selectOrdersTotal = createSelector(
  selectAllOrders,
  (orders) => orders.reduce((sum, o) => sum + o.totalAmount, 0)
);

// features/orders/store/orders.effects.ts
import { Injectable, inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { of } from 'rxjs';
import { map, catchError, switchMap, tap } from 'rxjs/operators';
import { OrdersApiService } from '../../../core/services/orders-api.service';
import { OrdersActions } from './orders.actions';
import { Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';

@Injectable()
export class OrdersEffects {
  private readonly actions$ = inject(Actions);
  private readonly ordersApi = inject(OrdersApiService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  loadOrders$ = createEffect(() =>
    this.actions$.pipe(
      ofType(OrdersActions.loadOrders),
      switchMap(({ params }) =>
        this.ordersApi.getOrders(params).pipe(
          map(response => OrdersActions.loadOrdersSuccess({ response })),
          catchError(error => 
            of(OrdersActions.loadOrdersFailure({ error: error.message }))
          )
        )
      )
    )
  );

  loadOrderDetails$ = createEffect(() =>
    this.actions$.pipe(
      ofType(OrdersActions.loadOrderDetails),
      switchMap(({ id }) =>
        this.ordersApi.getOrderById(id).pipe(
          map(order => OrdersActions.loadOrderDetailsSuccess({ order })),
          catchError(error =>
            of(OrdersActions.loadOrderDetailsFailure({ error: error.message }))
          )
        )
      )
    )
  );

  createOrder$ = createEffect(() =>
    this.actions$.pipe(
      ofType(OrdersActions.createOrder),
      switchMap(({ request }) =>
        this.ordersApi.createOrder(request).pipe(
          map(order => OrdersActions.createOrderSuccess({ order })),
          catchError(error =>
            of(OrdersActions.createOrderFailure({ error: error.message }))
          )
        )
      )
    )
  );

  createOrderSuccess$ = createEffect(
    () =>
      this.actions$.pipe(
        ofType(OrdersActions.createOrderSuccess),
        tap(({ order }) => {
          this.snackBar.open('Ordine creato con successo!', 'Chiudi', {
            duration: 3000
          });
          this.router.navigate(['/orders', order.id]);
        })
      ),
    { dispatch: false }
  );

  updateOrderStatus$ = createEffect(() =>
    this.actions$.pipe(
      ofType(OrdersActions.updateOrderStatus),
      switchMap(({ id, status }) =>
        this.ordersApi.updateOrderStatus(id, status).pipe(
          map(() => OrdersActions.updateOrderStatusSuccess({ id, status })),
          catchError(error =>
            of(OrdersActions.updateOrderStatusFailure({ error: error.message }))
          )
        )
      )
    )
  );

  deleteOrder$ = createEffect(() =>
    this.actions$.pipe(
      ofType(OrdersActions.deleteOrder),
      switchMap(({ id }) =>
        this.ordersApi.deleteOrder(id).pipe(
          map(() => OrdersActions.deleteOrderSuccess({ id })),
          catchError(error =>
            of(OrdersActions.deleteOrderFailure({ error: error.message }))
          )
        )
      )
    )
  );

  showError$ = createEffect(
    () =>
      this.actions$.pipe(
        ofType(
          OrdersActions.loadOrdersFailure,
          OrdersActions.createOrderFailure,
          OrdersActions.updateOrderStatusFailure
        ),
        tap(({ error }) => {
          this.snackBar.open(error, 'Chiudi', {
            duration: 5000,
            panelClass: ['error-snackbar']
          });
        })
      ),
    { dispatch: false }
  );
}
/* NgRx State Management */
