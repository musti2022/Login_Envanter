import { assetStatuses, assetTypes, type AssetStatus, type AssetType } from './labels'
import { defaultListParams, sortFields, type AssetListParams, type SortField } from './assetsApi'

export const pageSizes = [10, 25, 50, 100] as const

function positiveInteger(value: string | null, max = Number.MAX_SAFE_INTEGER): number | null {
  if (value === null || !/^\d+$/.test(value)) return null
  const number = Number(value)
  return number >= 1 && number <= max ? number : null
}

/**
 * Reads the list parameters from the page address. Anything unknown or out of range (a hand-edited link)
 * falls back to the default instead of producing a request the API would refuse.
 */
export function parseListParams(search: URLSearchParams): AssetListParams {
  const sortBy = search.get('sortBy')
  const pageSize = positiveInteger(search.get('pageSize'))
  const brandId = positiveInteger(search.get('brandId'))
  const cityId = positiveInteger(search.get('cityId'))
  return {
    page: positiveInteger(search.get('page'), 100_000) ?? defaultListParams.page,
    pageSize: pageSize !== null && (pageSizes as readonly number[]).includes(pageSize) ? pageSize : defaultListParams.pageSize,
    sortBy: sortFields.includes(sortBy as SortField) ? (sortBy as SortField) : defaultListParams.sortBy,
    sortDirection: search.get('sortDirection') === 'desc' ? 'desc' : 'asc',
    search: (search.get('search') ?? '').slice(0, 100),
    status: unique(search.getAll('status').filter((s): s is AssetStatus => (assetStatuses as readonly string[]).includes(s))),
    assetType: unique(search.getAll('assetType').filter((t): t is AssetType => (assetTypes as readonly string[]).includes(t))),
    brandId,
    // The filters choose a model within a brand and a location within a city; without the brand or the city
    // the screen could not show the choice, so it is not applied.
    modelId: brandId === null ? null : positiveInteger(search.get('modelId')),
    cityId,
    departmentId: positiveInteger(search.get('departmentId')),
    locationId: cityId === null ? null : positiveInteger(search.get('locationId')),
    archived: search.get('archived') === 'true',
  }
}

function unique<T>(values: T[]): T[] {
  return [...new Set(values)]
}

/** The filters a list has on (search included); sorting and paging do not count. */
export function activeFilterCount(params: AssetListParams): number {
  return [
    params.search.trim() !== '',
    params.status.length > 0,
    params.assetType.length > 0,
    params.brandId !== null,
    params.modelId !== null,
    params.cityId !== null,
    params.locationId !== null,
    params.departmentId !== null,
    params.archived,
  ].filter(Boolean).length
}

/** Every filter off, back on page 1; sorting and page size stay. */
export const clearedFilters: Partial<AssetListParams> = {
  search: '',
  status: [],
  assetType: [],
  brandId: null,
  modelId: null,
  cityId: null,
  locationId: null,
  departmentId: null,
  archived: false,
  page: 1,
}
