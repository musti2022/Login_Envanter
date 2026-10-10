import { apiDownload, apiFetch } from '../api/http'
import { defaultListParams, toQueryString, type AssetListParams } from '../inventory/assetsApi'
import type { AssetType } from '../inventory/labels'

export const reportsQueryKey = ['reports'] as const

/** What the inventory summary counts assets by; the API takes these names. */
export const assetGroupings = ['city', 'department', 'location', 'brand', 'model', 'assetType', 'status'] as const
export type AssetGrouping = (typeof assetGroupings)[number]

export const groupingLabels: Record<AssetGrouping, string> = {
  city: 'Şehir',
  department: 'Departman',
  location: 'Lokasyon',
  brand: 'Marka',
  model: 'Model',
  assetType: 'Tür',
  status: 'Durum',
}

/** A group's assets by status; inventoryFilter is the inventory query of the same assets (null when there is none). */
export interface AssetSummaryRow {
  key: string
  name: string
  inventoryFilter: Record<string, string> | null
  totalCount: number
  assignedCount: number
  availableCount: number
  faultyCount: number
  retiredCount: number
}

export interface AssetSummary {
  rows: AssetSummaryRow[]
  total: AssetSummaryRow
}

/** "Envanter özeti": the inventory filters (archive, sorting and paging do not apply) and a grouping. */
export interface SummaryParams {
  groupBy: AssetGrouping
  filters: AssetListParams
}

/** The query string of GET /api/reports/asset-summary, which is also the summary page's address; defaults left out. */
export function summaryQueryString({ groupBy, filters }: SummaryParams): string {
  const query = new URLSearchParams(
    toQueryString({ ...filters, archived: false, sortBy: 'assetCode', sortDirection: 'asc', page: 1, pageSize: 25 }),
  )
  if (groupBy !== 'city') query.set('groupBy', groupBy)
  return query.toString()
}

export function fetchAssetSummary(params: SummaryParams, signal?: AbortSignal) {
  const query = summaryQueryString(params)
  return apiFetch<AssetSummary>(`/api/reports/asset-summary${query ? `?${query}` : ''}`, { signal })
}

export function exportAssetSummary(params: SummaryParams, signal?: AbortSignal) {
  const query = summaryQueryString(params)
  return apiDownload(`/api/reports/asset-summary/export${query ? `?${query}` : ''}`, 'envanter-ozeti.xlsx', signal)
}

/**
 * The inventory list of a row's assets: the report's filters, with the row's own filter taking the place of a filter
 * of the same name (a "Dizüstü" row of a report on two types lists laptops only).
 */
export function inventoryLink(filters: AssetListParams, rowFilter: Record<string, string>): string {
  const list: AssetListParams = { ...defaultListParams, ...filters, archived: false, page: 1 }
  for (const [name, value] of Object.entries(rowFilter)) {
    if (name === 'status') list.status = [value as AssetListParams['status'][number]]
    else if (name === 'assetType') list.assetType = [value as AssetType]
    else if (name === 'brandId' || name === 'modelId' || name === 'cityId' || name === 'locationId' || name === 'departmentId') {
      list[name] = Number(value)
    }
  }
  const query = toQueryString({ ...list, sortBy: defaultListParams.sortBy, sortDirection: defaultListParams.sortDirection })
  return `/envanter${query ? `?${query}` : ''}`
}

export const movementKinds = ['Assigned', 'Returned'] as const
export type MovementKind = (typeof movementKinds)[number]

export const movementLabels: Record<MovementKind, string> = {
  Assigned: 'Zimmet verildi',
  Returned: 'İade alındı',
}

export interface AssignmentMovement {
  assignmentId: number
  movement: MovementKind
  at: string
  by: string
  assetId: number
  assetCode: string
  assetType: AssetType
  brandName: string
  modelName: string
  serialNumber: string | null
  employeeUserName: string
  employeeDisplayName: string
  assignmentDescription: string | null
  cityName: string
  departmentName: string
  assetArchived: boolean
}

export interface AssignmentReport {
  items: AssignmentMovement[]
  page: number
  pageSize: number
  totalCount: number
  assignedCount: number
  returnedCount: number
}

/** "Zimmet hareketleri", as the page keeps it in its address. Days are YYYY-MM-DD, both included; '' for none. */
export interface MovementParams {
  page: number
  pageSize: number
  from: string
  to: string
  movement: MovementKind | null
  search: string
  assetType: AssetType[]
  cityId: number | null
  departmentId: number | null
}

export const defaultMovementParams: MovementParams = {
  page: 1,
  pageSize: 25,
  from: '',
  to: '',
  movement: null,
  search: '',
  assetType: [],
  cityId: null,
  departmentId: null,
}

/** The query string of GET /api/reports/assignments, which is also the page's address; defaults left out. */
export function movementQueryString(params: MovementParams, { paging = true } = {}): string {
  const query = new URLSearchParams()
  if (params.from) query.set('from', params.from)
  if (params.to) query.set('to', params.to)
  if (params.movement) query.set('movement', params.movement)
  const search = params.search.trim()
  if (search) query.set('search', search)
  params.assetType.forEach((type) => query.append('assetType', type))
  if (params.cityId !== null) query.set('cityId', String(params.cityId))
  if (params.departmentId !== null) query.set('departmentId', String(params.departmentId))
  if (paging && params.page !== defaultMovementParams.page) query.set('page', String(params.page))
  if (paging && params.pageSize !== defaultMovementParams.pageSize) query.set('pageSize', String(params.pageSize))
  return query.toString()
}

export function fetchAssignmentReport(params: MovementParams, signal?: AbortSignal) {
  const query = movementQueryString(params)
  return apiFetch<AssignmentReport>(`/api/reports/assignments${query ? `?${query}` : ''}`, { signal })
}

/** Every movement that matches, as an .xlsx file; the page on screen does not narrow it. */
export function exportAssignmentReport(params: MovementParams, signal?: AbortSignal) {
  const query = movementQueryString(params, { paging: false })
  return apiDownload(`/api/reports/assignments/export${query ? `?${query}` : ''}`, 'zimmet-hareketleri.xlsx', signal)
}
