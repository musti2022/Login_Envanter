import { pageSizes } from '../inventory/listParams'
import {
  auditActions,
  auditEntityNames,
  defaultAuditLogParams,
  startOfDay,
  type AuditAction,
  type AuditEntityName,
  type AuditLogParams,
} from './auditApi'

const dayPattern = /^\d{4}-\d{2}-\d{2}$/

function positiveInteger(value: string | null, max: number): number | null {
  if (value === null || !/^\d+$/.test(value)) return null
  const number = Number(value)
  return number >= 1 && number <= max ? number : null
}

/** A real calendar day in YYYY-MM-DD, or ''. */
function day(value: string | null): string {
  if (value === null || !dayPattern.test(value)) return ''
  const date = startOfDay(value)
  const [year, month, dayOfMonth] = value.split('-').map(Number)
  return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === dayOfMonth ? value : ''
}

/**
 * Reads the audit filters from the page address. Anything unknown or out of range (a hand-edited link) falls back to
 * the default instead of producing a request the API would refuse.
 */
export function parseAuditParams(search: URLSearchParams): AuditLogParams {
  const pageSize = positiveInteger(search.get('pageSize'), 100)
  const entityName = search.get('entityName')
  const knownEntity = (auditEntityNames as readonly string[]).includes(entityName ?? '') ? (entityName as AuditEntityName) : null
  const entityId = (search.get('entityId') ?? '').trim()
  const fromDate = day(search.get('from'))
  const toDate = day(search.get('to'))
  return {
    page: positiveInteger(search.get('page'), 100_000) ?? defaultAuditLogParams.page,
    pageSize:
      pageSize !== null && (pageSizes as readonly number[]).includes(pageSize) ? pageSize : defaultAuditLogParams.pageSize,
    entityName: knownEntity,
    // A record ID means something only for its kind of record.
    entityId: knownEntity && entityId && entityId.length <= 64 ? entityId : null,
    assetCode: (search.get('assetCode') ?? '').slice(0, 50),
    action: [...new Set(search.getAll('action'))].filter((a): a is AuditAction =>
      (auditActions as readonly string[]).includes(a),
    ),
    userName: (search.get('userName') ?? '').slice(0, 100),
    fromDate,
    // A window that ends before it starts would be refused; the end is dropped.
    toDate: fromDate && toDate && toDate < fromDate ? '' : toDate,
    correlationId: (search.get('correlationId') ?? '').slice(0, 64),
  }
}

/** The filters that are on; paging does not count. */
export function activeAuditFilterCount(params: AuditLogParams): number {
  return [
    params.entityName !== null,
    params.entityId !== null,
    params.assetCode.trim() !== '',
    params.action.length > 0,
    params.userName.trim() !== '',
    params.fromDate !== '',
    params.toDate !== '',
    params.correlationId.trim() !== '',
  ].filter(Boolean).length
}

/** Every filter off, back on page 1; the page size stays. */
export const clearedAuditFilters: Partial<AuditLogParams> = {
  entityName: null,
  entityId: null,
  assetCode: '',
  action: [],
  userName: '',
  fromDate: '',
  toDate: '',
  correlationId: '',
  page: 1,
}
