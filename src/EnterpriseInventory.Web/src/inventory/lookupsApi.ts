import { queryOptions } from '@tanstack/react-query'
import { apiFetch } from '../api/http'

/** A brand, city or department. Inactive ones are listed too: older assets still carry them. */
export interface LookupItem {
  id: number
  name: string
  isActive: boolean
}

export interface ModelItem extends LookupItem {
  brandId: number
  brandName: string
}

export interface LocationItem extends LookupItem {
  cityId: number
  cityName: string
}

export const lookupsQueryKey = ['lookups'] as const

/** Lookups change rarely; a list fetched in the last five minutes is used as is. */
const staleTime = 5 * 60_000

export const brandsQuery = queryOptions({
  queryKey: [...lookupsQueryKey, 'brands'],
  queryFn: ({ signal }) => apiFetch<LookupItem[]>('/api/brands', { signal }),
  staleTime,
})

export const citiesQuery = queryOptions({
  queryKey: [...lookupsQueryKey, 'cities'],
  queryFn: ({ signal }) => apiFetch<LookupItem[]>('/api/cities', { signal }),
  staleTime,
})

export const departmentsQuery = queryOptions({
  queryKey: [...lookupsQueryKey, 'departments'],
  queryFn: ({ signal }) => apiFetch<LookupItem[]>('/api/departments', { signal }),
  staleTime,
})

export function modelsQuery(brandId: number | null) {
  return queryOptions({
    queryKey: [...lookupsQueryKey, 'models', brandId],
    queryFn: ({ signal }) => apiFetch<ModelItem[]>(`/api/models?brandId=${brandId}`, { signal }),
    enabled: brandId !== null,
    staleTime,
  })
}

export function locationsQuery(cityId: number | null) {
  return queryOptions({
    queryKey: [...lookupsQueryKey, 'locations', cityId],
    queryFn: ({ signal }) => apiFetch<LocationItem[]>(`/api/locations?cityId=${cityId}`, { signal }),
    enabled: cityId !== null,
    staleTime,
  })
}

/** The name as a list shows it: inactive lookups are marked. */
export function lookupLabel(item: LookupItem) {
  return item.isActive ? item.name : `${item.name} (pasif)`
}
