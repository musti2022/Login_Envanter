import {
  DefaultHttpClient,
  HttpClient,
  HttpError,
  HubConnectionBuilder,
  LogLevel,
  NullLogger,
  type HttpRequest,
  type HttpResponse,
  type HubConnection,
} from '@microsoft/signalr'
import { csrfHeaderName, csrfTokenFor, renewCsrfToken } from '../api/http'

/** The API's live notification hub; same origin as the app, like /api. */
export const inventoryHubPath = '/hubs/inventory'

/** What the hub announces. Each carries only which asset changed and when; the screens read the asset from the API. */
export const assetEventNames = [
  'AssetCreated',
  'AssetUpdated',
  'AssetArchived',
  'AssetAssigned',
  'AssetReturned',
  'AssetLocationChanged',
] as const

export type AssetEventName = (typeof assetEventNames)[number]

export interface AssetNotification {
  assetId: number
  occurredAt: string
}

/** The part of a SignalR connection the app uses; tests replace it with a fake. */
export type InventoryConnection = Pick<HubConnection, 'start' | 'stop' | 'on' | 'onclose'>

/**
 * Sends the hub's state-changing requests (negotiation, Long Polling sends) with the CSRF token, as apiFetch does
 * for the API, and renews a token the API refused once. WebSockets and Server-Sent Events only use GET. A request
 * refused because the session has ended (401) is reported, as apiFetch reports it: trying again cannot help.
 */
export class CsrfHttpClient extends HttpClient {
  private readonly inner: HttpClient
  private readonly onUnauthorized: () => void

  constructor(onUnauthorized: () => void, inner?: HttpClient) {
    super()
    this.onUnauthorized = onUnauthorized
    this.inner = inner ?? new DefaultHttpClient(NullLogger.instance)
  }

  override async send(request: HttpRequest): Promise<HttpResponse> {
    try {
      return await this.sendWithToken(request)
    } catch (error) {
      if (error instanceof HttpError && error.statusCode === 401) {
        this.onUnauthorized()
      }
      throw error
    }
  }

  private async sendWithToken(request: HttpRequest): Promise<HttpResponse> {
    if (request.method === 'GET') {
      return this.inner.send(request)
    }

    try {
      return await this.inner.send(withToken(request, await csrfTokenFor()))
    } catch (error) {
      if (error instanceof HttpError && error.statusCode === 400 && error.message.includes('csrf_invalid')) {
        return this.inner.send(withToken(request, await renewCsrfToken()))
      }
      throw error
    }
  }

  override getCookieString(url: string): string {
    return this.inner.getCookieString(url)
  }
}

function withToken(request: HttpRequest, token: string): HttpRequest {
  return { ...request, headers: { ...request.headers, [csrfHeaderName]: token } }
}

export interface InventoryConnectionOptions {
  /** The API refused the connection because the session has ended. */
  onUnauthorized: () => void
}

/**
 * A connection to the hub. It does not reconnect by itself (no withAutomaticReconnect): useLiveUpdates starts it
 * again after a failed start and after a lost connection alike, with one backoff (see reconnect.ts).
 */
export function createInventoryConnection({ onUnauthorized }: InventoryConnectionOptions): InventoryConnection {
  return new HubConnectionBuilder()
    .withUrl(inventoryHubPath, { httpClient: new CsrfHttpClient(onUnauthorized) })
    .configureLogging(LogLevel.Warning)
    .build()
}
