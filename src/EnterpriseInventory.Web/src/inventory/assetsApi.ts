import { ApiError, apiFetch } from '../api/http'
import type { AssetStatus, AssetType } from './labels'

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

/** A row of GET /api/assets. */
export interface AssetListItem {
  id: number
  assetCode: string
  computerName: string | null
  brandName: string
  modelName: string
  serialNumber: string | null
  assetType: AssetType
  status: AssetStatus
  cityName: string
  departmentName: string
  locationName: string | null
  assignedUserName: string | null
  assignedDisplayName: string | null
  assignmentDescription: string | null
  isArchived: boolean
  createdAt: string
  updatedAt: string | null
}

export const sortFields = [
  'assetCode',
  'computerName',
  'serialNumber',
  'brandName',
  'modelName',
  'assetType',
  'status',
  'cityName',
  'departmentName',
  'locationName',
  'assignedUserName',
  'assignedDisplayName',
  'createdAt',
  'updatedAt',
] as const
export type SortField = (typeof sortFields)[number]
export type SortDirection = 'asc' | 'desc'

/** The list request, as the inventory page keeps it in its address (see listParams.ts). */
export interface AssetListParams {
  page: number
  pageSize: number
  sortBy: SortField
  sortDirection: SortDirection
  search: string
  status: AssetStatus[]
  assetType: AssetType[]
  brandId: number | null
  modelId: number | null
  cityId: number | null
  departmentId: number | null
  locationId: number | null
  archived: boolean
}

export const defaultListParams: AssetListParams = {
  page: 1,
  pageSize: 25,
  sortBy: 'assetCode',
  sortDirection: 'asc',
  search: '',
  status: [],
  assetType: [],
  brandId: null,
  modelId: null,
  cityId: null,
  departmentId: null,
  locationId: null,
  archived: false,
}

/**
 * The query string of GET /api/assets for these parameters, defaults left out. The inventory page uses the
 * same string as its own address, so a copied link opens the same list.
 */
export function toQueryString(params: AssetListParams): string {
  const query = new URLSearchParams()
  const search = params.search.trim()
  if (search) query.set('search', search)
  params.status.forEach((status) => query.append('status', status))
  params.assetType.forEach((type) => query.append('assetType', type))
  for (const key of ['brandId', 'modelId', 'cityId', 'departmentId', 'locationId'] as const) {
    const value = params[key]
    if (value !== null) query.set(key, String(value))
  }
  if (params.archived) query.set('archived', 'true')
  if (params.sortBy !== defaultListParams.sortBy) query.set('sortBy', params.sortBy)
  if (params.sortDirection !== defaultListParams.sortDirection) query.set('sortDirection', params.sortDirection)
  if (params.page !== defaultListParams.page) query.set('page', String(params.page))
  if (params.pageSize !== defaultListParams.pageSize) query.set('pageSize', String(params.pageSize))
  return query.toString()
}

export const assetsQueryKey = ['assets'] as const

export function fetchAssets(params: AssetListParams, signal?: AbortSignal) {
  const query = toQueryString(params)
  return apiFetch<PagedResult<AssetListItem>>(`/api/assets${query ? `?${query}` : ''}`, { signal })
}

export interface NamedReference {
  id: number
  name: string
}

/** Who holds the asset now. */
export interface ActiveAssignment {
  id: number
  employeeId: number
  userName: string
  displayName: string
  assignmentDescription: string | null
  assignedAt: string
  assignedBy: string
}

/** GET /api/assets/{id}: everything about one asset, with the row version an update or archive sends back. */
export interface AssetDetails {
  id: number
  assetCode: string
  computerName: string | null
  assetType: AssetType
  status: AssetStatus
  serialNumber: string | null
  description: string | null
  brand: NamedReference
  model: NamedReference
  city: NamedReference
  department: NamedReference
  location: NamedReference | null
  activeAssignment: ActiveAssignment | null
  isArchived: boolean
  createdAt: string
  createdBy: string
  updatedAt: string | null
  updatedBy: string | null
  rowVersion: string
}

/** Body of POST /api/assets; PUT adds the row version. The brand is the model's. */
export interface SaveAssetBody {
  assetCode: string
  assetType: AssetType
  status: AssetStatus
  modelId: number
  cityId: number
  departmentId: number
  locationId: number | null
  computerName: string | null
  serialNumber: string | null
  description: string | null
}

export const assetQueryKey = (id: number) => [...assetsQueryKey, 'detail', id] as const

/** Query retry rule for one asset: a missing asset stays missing, anything else is tried once more. */
export function retryUnlessNotFound(failureCount: number, error: unknown) {
  return !(error instanceof ApiError && error.status === 404) && failureCount < 1
}

export function fetchAsset(id: number, signal?: AbortSignal) {
  return apiFetch<AssetDetails>(`/api/assets/${id}`, { signal })
}

export function createAsset(body: SaveAssetBody) {
  return apiFetch<AssetDetails>('/api/assets', { method: 'POST', body })
}

export function updateAsset(id: number, body: SaveAssetBody, rowVersion: string) {
  return apiFetch<AssetDetails>(`/api/assets/${id}`, { method: 'PUT', body: { ...body, rowVersion } })
}

/** Archives (soft-deletes) the asset; refused with 409 when someone changed it since rowVersion was read. */
export function archiveAsset(id: number, rowVersion: string) {
  return apiFetch<void>(`/api/assets/${id}?rowVersion=${encodeURIComponent(rowVersion)}`, { method: 'DELETE' })
}

/** One audit record of GET /api/assets/{id}/history: the fields an action changed, as JSON objects. */
export interface AssetHistoryEntry {
  id: number
  action: string
  userName: string
  timestamp: string
  correlationId: string
  oldValues: Record<string, unknown> | null
  newValues: Record<string, unknown> | null
}

export const historyPageSize = 10

export const assetHistoryQueryKey = (id: number, page: number) => [...assetsQueryKey, 'history', id, page] as const

export function fetchAssetHistory(id: number, page: number, signal?: AbortSignal) {
  return apiFetch<PagedResult<AssetHistoryEntry>>(`/api/assets/${id}/history?page=${page}&pageSize=${historyPageSize}`, { signal })
}
