// core/services/signalr.service.ts
import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Observable, Subject, BehaviorSubject } from 'rxjs';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class SignalRService {
  private hubConnection: signalR.HubConnection | null = null;
  private connectionState$ = new BehaviorSubject<signalR.HubConnectionState>(
    signalR.HubConnectionState.Disconnected
  );

  // Eventi real-time
  private orderCreated$ = new Subject<Order>();
  private orderUpdated$ = new Subject<Order>();
  private orderDeleted$ = new Subject<number>();

  get orderCreated(): Observable<Order> {
    return this.orderCreated$.asObservable();
  }

  get orderUpdated(): Observable<Order> {
    return this.orderUpdated$.asObservable();
  }

  get orderDeleted(): Observable<number> {
    return this.orderDeleted$.asObservable();
  }

  get connectionState(): Observable<signalR.HubConnectionState> {
    return this.connectionState$.asObservable();
  }

  async connect(accessToken: string): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiUrl}/hubs/orders`, {
        accessTokenFactory: () => accessToken,
        transport: signalR.HttpTransportType.WebSockets
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          if (retryContext.elapsedMilliseconds < 60000) {
            return Math.random() * 10000;
          } else {
            return null; // Stop retrying after 1 minute
          }
        }
      })
      .configureLogging(signalR.LogLevel.Information)
      .build();

    this.setupEventHandlers();

    try {
      await this.hubConnection.start();
      this.connectionState$.next(signalR.HubConnectionState.Connected);
      console.log('SignalR Connected');
      
      // Join groups
      await this.hubConnection.invoke('JoinOrdersGroup');
    } catch (error) {
      console.error('SignalR Connection Error:', error);
      this.connectionState$.next(signalR.HubConnectionState.Disconnected);
    }
  }

  async disconnect(): Promise<void> {
    if (this.hubConnection) {
      await this.hubConnection.stop();
      this.connectionState$.next(signalR.HubConnectionState.Disconnected);
    }
  }

  private setupEventHandlers(): void {
    if (!this.hubConnection) return;

    this.hubConnection.on('OrderCreated', (order: Order) => {
      console.log('Order Created:', order);
      this.orderCreated$.next(order);
    });

    this.hubConnection.on('OrderUpdated', (order: Order) => {
      console.log('Order Updated:', order);
      this.orderUpdated$.next(order);
    });

    this.hubConnection.on('OrderDeleted', (orderId: number) => {
      console.log('Order Deleted:', orderId);
      this.orderDeleted$.next(orderId);
    });

    this.hubConnection.onreconnecting(() => {
      console.log('SignalR Reconnecting...');
      this.connectionState$.next(signalR.HubConnectionState.Reconnecting);
    });

    this.hubConnection.onreconnected(() => {
      console.log('SignalR Reconnected');
      this.connectionState$.next(signalR.HubConnectionState.Connected);
    });

    this.hubConnection.onclose(() => {
      console.log('SignalR Connection Closed');
      this.connectionState$.next(signalR.HubConnectionState.Disconnected);
    });
  }
}
