import { vi } from 'vitest'

export interface RecordedRequest {
  method: string
  path: string
  headers: Record<string, string>
  body: unknown
}

type Reply = Response | (() => Response) | ((request: RecordedRequest) => Response | Promise<Response>)

/** A JSON (or problem+json) response like the API's. */
export function json(status: number, body?: unknown, headers: Record<string, string> = {}) {
  const contentType = status >= 400 ? 'application/problem+json' : 'application/json'
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: body === undefined ? headers : { 'Content-Type': contentType, ...headers },
  })
}

/**
 * Replaces fetch with fake API routes keyed by "METHOD /path" or "METHOD /path?query" (which wins). Unlisted
 * routes answer 404. Returns the requests made, in order.
 */
export function mockApi(routes: Record<string, Reply>) {
  const requests: RecordedRequest[] = []
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = typeof input === 'string' ? input : input instanceof URL ? input.pathname : input.url
    const method = (init?.method ?? 'GET').toUpperCase()
    const headers = Object.fromEntries(new Headers(init?.headers).entries())
    const request: RecordedRequest = {
      method,
      path,
      headers,
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
    }
    requests.push(request)

    // An exact match first ("GET /api/assets?page=2"), then the path without its query string.
    const reply = routes[`${method} ${path}`] ?? routes[`${method} ${path.split('?')[0]}`]
    if (!reply) {
      return json(404, { title: 'İstenen kaynak bulunamadı.', status: 404 })
    }
    return typeof reply === 'function' ? reply(request) : reply.clone()
  })
  vi.stubGlobal('fetch', fetchMock)
  return requests
}
