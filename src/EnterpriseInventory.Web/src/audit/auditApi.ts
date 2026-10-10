import { apiFetch } from '../api/http'
import type { PagedResult } from '../inventory/assetsApi'

/** The kinds of record the audit log covers, by the names the API stores. */
export const auditEntityNames = ['Asset', 'Brand', 'AssetModel', 'City', 'Department', 'Location', 'AdminUser'] as const
export type AuditEntityName = (typeof auditEntityNames)[number]

export const auditActions = [
  'Created',
  'Updated',
  'StatusChanged',
  'LocationChanged',
  'Assigned',
  'Returned',
  'Archived',
  'SignedIn',
  'SignedOut',
  'AccessRevoked',
] as const
export type AuditAction = (typeof auditActions)[number]

/** One record of GET /api/audit-logs: what changed, how, by whom, when, and the changed fields as JSON objects. */
export interface AuditLogEntry {
  id: number
  entityName: string
  entityId: string
  /** How the record is known now (an asset's code, a lookup's name); null when it no longer exists. */
  entityLabel: string | null
  action: string
  userName: string
  timestamp: string
  correlationId: string
  oldValues: Record<string, unknown> | null
  newValues: Record<string, unknown> | null
}

/**
 * The audit screen's filters, as its page address keeps them. Dates are whole local days (YYYY-MM-DD); the request
 * turns them into moments, so "to" includes its day.
 */
export interface AuditLogParams {
  page: number
  pageSize: number
  entityName: AuditEntityName | null
  entityId: string | null
  assetCode: string
  action: AuditAction[]
  userName: string
  fromDate: string
  toDate: string
  correlationId: string
}

export const defaultAuditLogParams: AuditLogParams = {
  page: 1,
  pageSize: 25,
  entityName: null,
  entityId: null,
  assetCode: '',
  action: [],
  userName: '',
  fromDate: '',
  toDate: '',
  correlationId: '',
}

/** The page's own address for these filters, defaults left out, so a copied link opens the same records. */
export function toPageQuery(params: AuditLogParams): string {
  const query = filterQuery(params)
  if (params.fromDate) query.set('from', params.fromDate)
  if (params.toDate) query.set('to', params.toDate)
  if (params.page !== defaultAuditLogParams.page) query.set('page', String(params.page))
  if (params.pageSize !== defaultAuditLogParams.pageSize) query.set('pageSize', String(params.pageSize))
  return query.toString()
}

/** The query string of GET /api/audit-logs: the days become the start of "from" and the end of "to" in local time. */
export function toApiQuery(params: AuditLogParams): string {
  const query = filterQuery(params)
  if (params.fromDate) query.set('from', startOfDay(params.fromDate).toISOString())
  if (params.toDate) {
    const end = startOfDay(params.toDate)
    end.setDate(end.getDate() + 1)
    query.set('to', end.toISOString())
  }
  query.set('page', String(params.page))
  query.set('pageSize', String(params.pageSize))
  return query.toString()
}

function filterQuery(params: AuditLogParams) {
  const query = new URLSearchParams()
  if (params.entityName) query.set('entityName', params.entityName)
  if (params.entityName && params.entityId) query.set('entityId', params.entityId)
  const assetCode = params.assetCode.trim()
  if (assetCode && (params.entityName === null || params.entityName === 'Asset')) query.set('assetCode', assetCode)
  params.action.forEach((action) => query.append('action', action))
  const userName = params.userName.trim()
  if (userName) query.set('userName', userName)
  const correlationId = params.correlationId.trim()
  if (correlationId) query.set('correlationId', correlationId)
  return query
}

/** Local midnight of a YYYY-MM-DD day. */
export function startOfDay(day: string) {
  const [year, month, date] = day.split('-').map(Number)
  return new Date(year, month - 1, date)
}

export const auditLogsQueryKey = ['audit-logs'] as const

export function fetchAuditLogs(params: AuditLogParams, signal?: AbortSignal) {
  return apiFetch<PagedResult<AuditLogEntry>>(`/api/audit-logs?${toApiQuery(params)}`, { signal })
}
