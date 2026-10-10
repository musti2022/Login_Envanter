import { calendarDay } from '../audit/auditParams'
import { assetTypes, type AssetType } from '../inventory/labels'
import { pageSizes, parseListParams } from '../inventory/listParams'
import {
  assetGroupings,
  defaultMovementParams,
  movementKinds,
  type AssetGrouping,
  type MovementKind,
  type MovementParams,
  type SummaryParams,
} from './reportsApi'

function positiveInteger(value: string | null, max = 2_147_483_647): number | null {
  if (value === null || !/^\d+$/.test(value)) return null
  const number = Number(value)
  return number >= 1 && number <= max ? number : null
}

/** The summary report from the page address: the inventory list's filters and a grouping (by city when unknown). */
export function parseSummaryParams(search: URLSearchParams): SummaryParams {
  const groupBy = search.get('groupBy')
  return {
    groupBy: (assetGroupings as readonly string[]).includes(groupBy ?? '') ? (groupBy as AssetGrouping) : 'city',
    filters: { ...parseListParams(search), archived: false },
  }
}

/**
 * The movement report from the page address. Anything unknown or out of range (a hand-edited link) falls back to
 * the default instead of producing a request the API would refuse.
 */
export function parseMovementParams(search: URLSearchParams): MovementParams {
  const pageSize = positiveInteger(search.get('pageSize'))
  const movement = search.get('movement')
  const from = calendarDay(search.get('from'))
  const to = calendarDay(search.get('to'))
  return {
    page: positiveInteger(search.get('page'), 100_000) ?? defaultMovementParams.page,
    pageSize:
      pageSize !== null && (pageSizes as readonly number[]).includes(pageSize) ? pageSize : defaultMovementParams.pageSize,
    from,
    // A period that ends before it starts would be refused; the end is dropped.
    to: from && to && to < from ? '' : to,
    movement: (movementKinds as readonly string[]).includes(movement ?? '') ? (movement as MovementKind) : null,
    search: (search.get('search') ?? '').slice(0, 100),
    assetType: [...new Set(search.getAll('assetType'))].filter((t): t is AssetType =>
      (assetTypes as readonly string[]).includes(t),
    ),
    cityId: positiveInteger(search.get('cityId')),
    departmentId: positiveInteger(search.get('departmentId')),
  }
}

/** The filters that are on; paging does not count. */
export function activeMovementFilterCount(params: MovementParams): number {
  return [
    params.from !== '',
    params.to !== '',
    params.movement !== null,
    params.search.trim() !== '',
    params.assetType.length > 0,
    params.cityId !== null,
    params.departmentId !== null,
  ].filter(Boolean).length
}

export const clearedMovementFilters: Partial<MovementParams> = {
  from: '',
  to: '',
  movement: null,
  search: '',
  assetType: [],
  cityId: null,
  departmentId: null,
  page: 1,
}

const day = (date: Date) =>
  `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`

/** "Bu ay" and "Geçen ay" as days of the browser's calendar, both ends included. */
export function monthPeriod(today: Date, monthsBack: number): { from: string; to: string } {
  const first = new Date(today.getFullYear(), today.getMonth() - monthsBack, 1)
  const last = new Date(today.getFullYear(), today.getMonth() - monthsBack + 1, 0)
  return { from: day(first), to: day(last) }
}
