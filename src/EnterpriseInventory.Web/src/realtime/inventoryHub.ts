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
export type InventoryConnection = Pick<HubConnection, 'start' | 'stop' | 'on' | 'onclose' | 'onreconnecting' | 'onreconnected'>

/**
 * Sends the hub's state-changing requests (negotiation, Long Polling sends) with the CSRF token, as apiFetch does
 * for the API, and renews a token the API refused once. WebSockets and Server-Sent Events only use GET.
 */
export class CsrfHttpClient extends HttpClient {
  private readonly inner: HttpClient

  constructor(inner?: HttpClient) {
    super()
    this.inner = inner ?? new DefaultHttpClient(NullLogger.instance)
  }

  override async send(request: HttpRequest): Promise<HttpResponse> {
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

export function createInventoryConnection(): InventoryConnection {
  return new HubConnectionBuilder()
    .withUrl(inventoryHubPath, { httpClient: new CsrfHttpClient() })
    .configureLogging(LogLevel.Warning)
    .build()
}
