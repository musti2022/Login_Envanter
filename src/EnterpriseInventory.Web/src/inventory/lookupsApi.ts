import { queryOptions } from '@tanstack/react-query'
import { apiFetch } from '../api/http'
import type { NamedReference } from './assetsApi'

/** A brand, city or department. Inactive ones are listed too: older assets still carry them. */
export interface LookupItem {
  id: number
  name: string
  isActive: boolean
  /** Sent back with a change, to prove which version was edited. */
  rowVersion: string
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

/** Every model, of every brand: the definitions screen lists them all. */
export const allModelsQuery = queryOptions({
  queryKey: [...lookupsQueryKey, 'models', 'all'],
  queryFn: ({ signal }) => apiFetch<ModelItem[]>('/api/models', { signal }),
  staleTime,
})

/** Every location, of every city. */
export const allLocationsQuery = queryOptions({
  queryKey: [...lookupsQueryKey, 'locations', 'all'],
  queryFn: ({ signal }) => apiFetch<LocationItem[]>('/api/locations', { signal }),
  staleTime,
})

/** A rename, deactivation or reactivation. A model keeps its brand and a location its city. */
export interface LookupChange {
  name: string
  isActive: boolean
  rowVersion: string
}

/** `PUT /api/brands/1` and the like; a stale row version is refused with `409 concurrency_conflict`. */
export function updateLookup<T extends LookupItem>(path: string, id: number, change: LookupChange) {
  return apiFetch<T>(`${path}/${id}`, { method: 'PUT', body: change })
}

/** The name as a list shows it: inactive lookups are marked. */
export function lookupLabel(item: LookupItem) {
  return item.isActive ? item.name : `${item.name} (pasif)`
}

/** A choice of a select: the lookup's ID as text, as the inputs hold it. */
export interface LookupOption {
  value: string
  label: string
}

/**
 * Active values, plus the asset's current one (also while the list is loading, so the select can show it): an
 * asset may keep a value that has since been deactivated, a new choice may not be inactive.
 */
export function lookupChoices(items: LookupItem[] | undefined, current: NamedReference | null | undefined): LookupOption[] {
  const options = (items ?? [])
    .filter((item) => item.isActive || item.id === current?.id)
    .map((item) => ({ value: String(item.id), label: lookupLabel(item) }))
  if (current && !options.some((option) => option.value === String(current.id))) {
    options.unshift({ value: String(current.id), label: current.name })
  }
  return options
}
