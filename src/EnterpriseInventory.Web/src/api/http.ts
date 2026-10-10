/**
 * The single way the app talks to the API. Requests go to the same origin with the session cookie; the
 * cookie is HttpOnly, so scripts never see it. State-changing requests carry the CSRF token in the
 * X-CSRF-TOKEN header. The token is kept in memory only, never in browser storage.
 */

/** RFC 7807 error body returned by the API, with its `code` extension. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  code?: string
  correlationId?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | undefined
  readonly retryAfterSeconds: number | undefined

  constructor(status: number, problem?: ProblemDetails, retryAfterSeconds?: number) {
    super(problem?.title ?? `HTTP ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.retryAfterSeconds = retryAfterSeconds
  }

  get code(): string | undefined {
    return this.problem?.code
  }
}

export const csrfHeaderName = 'X-CSRF-TOKEN'

/**
 * Marks a read the user did not ask for (a refresh after a live notification): the API still checks the session but
 * does not count the request as activity, so an unattended screen cannot keep an idle session alive.
 */
export const backgroundHeaderName = 'X-Background-Request'

const csrfPath = '/api/auth/csrf'
const safeMethods = new Set(['GET', 'HEAD', 'OPTIONS'])

let csrfToken: string | null = null
let unauthorizedHandler: (() => void) | null = null
let backgroundDepth = 0

/** Replaces the CSRF token, e.g. with the one the sign-in response carries; null forgets it. */
export function setCsrfToken(token: string | null) {
  csrfToken = token
}

/** Called when a request is refused because the session has ended. Returns a function that removes it. */
export function onUnauthorized(handler: () => void): () => void {
  unauthorizedHandler = handler
  return () => {
    if (unauthorizedHandler === handler) {
      unauthorizedHandler = null
    }
  }
}

/** Reports a request refused because the session has ended, made outside apiFetch (the live connection). */
export function reportUnauthorized() {
  unauthorizedHandler?.()
}

/**
 * Runs work whose reads are background reads (see backgroundHeaderName). Only requests sent while the work runs
 * synchronously are marked: TanStack Query starts the refetches of invalidateQueries before it returns.
 */
export function inBackground<T>(work: () => T): T {
  backgroundDepth++
  try {
    return work()
  } finally {
    backgroundDepth--
  }
}

/** The CSRF token for a state-changing request made outside apiFetch (the live connection); fetched if missing. */
export function csrfTokenFor(signal?: AbortSignal): Promise<string> {
  return csrfToken ? Promise.resolve(csrfToken) : fetchCsrfToken(signal)
}

/** Fetches a new CSRF token after the API refused the one in hand. */
export function renewCsrfToken(signal?: AbortSignal): Promise<string> {
  return fetchCsrfToken(signal)
}

export interface ApiRequestOptions {
  method?: string
  body?: unknown
  signal?: AbortSignal
  /** For the sign-in and session endpoints, whose 401 is an answer rather than an ended session. */
  ignoreUnauthorized?: boolean
}

export async function apiFetch<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  const method = (options.method ?? 'GET').toUpperCase()

  let response: Response
  if (safeMethods.has(method)) {
    response = await send(path, method, options)
  } else {
    response = await send(path, method, options, csrfToken ?? (await fetchCsrfToken(options.signal)))
    if (await isCsrfRejection(response)) {
      // The token belonged to an earlier sign-in (or none); get a fresh one and try once more.
      response = await send(path, method, options, await fetchCsrfToken(options.signal))
    }
  }

  if (response.status === 401 && !options.ignoreUnauthorized) {
    reportUnauthorized()
  }

  if (!response.ok) {
    throw await toApiError(response)
  }

  if (response.status === 204 || response.headers.get('Content-Length') === '0') {
    return undefined as T
  }

  return (await response.json()) as T
}

/** A file the API sent as an attachment. */
export interface DownloadedFile {
  blob: Blob
  fileName: string
}

/**
 * GETs a file (an Excel export). Errors arrive as ProblemDetails and are thrown as ApiError, like apiFetch's; the
 * file name is the one the API gives in Content-Disposition.
 */
export async function apiDownload(path: string, fallbackName: string, signal?: AbortSignal): Promise<DownloadedFile> {
  const response = await fetch(path, {
    headers: { Accept: '*/*' },
    credentials: 'same-origin',
    signal,
  })

  if (response.status === 401) {
    reportUnauthorized()
  }

  if (!response.ok) {
    throw await toApiError(response)
  }

  return { blob: await response.blob(), fileName: fileNameOf(response.headers.get('Content-Disposition')) ?? fallbackName }
}

/** The file name of a Content-Disposition header: the UTF-8 form (filename*) when given, else the plain one. */
export function fileNameOf(header: string | null): string | null {
  if (!header) return null
  const encoded = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(header)
  if (encoded) {
    try {
      return decodeURIComponent(encoded[1].trim())
    } catch {
      // Falls back to the plain name below.
    }
  }
  const plain = /filename\s*=\s*("([^"]*)"|[^;]+)/i.exec(header)
  return plain ? (plain[2] ?? plain[1]).trim() : null
}

function send(path: string, method: string, options: ApiRequestOptions, token?: string) {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  if (token) {
    headers[csrfHeaderName] = token
  }
  if (backgroundDepth > 0 && safeMethods.has(method)) {
    headers[backgroundHeaderName] = '1'
  }

  return fetch(path, {
    method,
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    credentials: 'same-origin',
    signal: options.signal,
  })
}

async function fetchCsrfToken(signal?: AbortSignal): Promise<string> {
  const response = await fetch(csrfPath, { headers: { Accept: 'application/json' }, credentials: 'same-origin', signal })
  if (!response.ok) {
    throw await toApiError(response)
  }

  const { token } = (await response.json()) as { token: string }
  csrfToken = token
  return token
}

async function isCsrfRejection(response: Response) {
  if (response.status !== 400) {
    return false
  }

  const problem = await readProblem(response.clone())
  return problem?.code === 'csrf_invalid'
}

async function toApiError(response: Response) {
  const retryAfter = Number.parseInt(response.headers.get('Retry-After') ?? '', 10)
  return new ApiError(response.status, await readProblem(response), Number.isFinite(retryAfter) ? retryAfter : undefined)
}

async function readProblem(response: Response): Promise<ProblemDetails | undefined> {
  const contentType = response.headers.get('Content-Type') ?? ''
  if (!contentType.includes('json')) {
    return undefined
  }

  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return undefined
  }
}
