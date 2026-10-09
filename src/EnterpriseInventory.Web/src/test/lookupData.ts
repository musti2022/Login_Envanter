import type { LocationItem, LookupItem, ModelItem } from '../inventory/lookupsApi'
import { json } from './mockApi'

export const brands: LookupItem[] = [
  { id: 1, name: 'Dell', isActive: true },
  { id: 2, name: 'Eski Marka', isActive: false },
  { id: 3, name: 'HP', isActive: true },
]

export const models: ModelItem[] = [
  { id: 11, name: 'Latitude 5440', isActive: true, brandId: 1, brandName: 'Dell' },
  { id: 12, name: 'Optiplex 7010', isActive: true, brandId: 1, brandName: 'Dell' },
  { id: 31, name: 'EliteBook 840', isActive: true, brandId: 3, brandName: 'HP' },
]

export const cities: LookupItem[] = [
  { id: 5, name: 'Ankara', isActive: true },
  { id: 6, name: 'İstanbul', isActive: true },
]

export const locations: LocationItem[] = [
  { id: 60, name: 'Merkez Ofis', isActive: true, cityId: 6, cityName: 'İstanbul' },
  { id: 61, name: 'Depo', isActive: false, cityId: 6, cityName: 'İstanbul' },
]

export const departments: LookupItem[] = [
  { id: 8, name: 'Bilgi İşlem', isActive: true },
  { id: 9, name: 'Muhasebe', isActive: true },
]

/** mockApi routes for every lookup list, with models and locations filtered by their parent. */
export const lookupRoutes = {
  'GET /api/brands': json(200, brands),
  'GET /api/cities': json(200, cities),
  'GET /api/departments': json(200, departments),
  'GET /api/models': ({ path }: { path: string }) =>
    json(200, models.filter((m) => m.brandId === Number(new URLSearchParams(path.split('?')[1]).get('brandId')))),
  'GET /api/locations': ({ path }: { path: string }) =>
    json(200, locations.filter((l) => l.cityId === Number(new URLSearchParams(path.split('?')[1]).get('cityId')))),
}
