import { apiFetch } from '../api/http'
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
