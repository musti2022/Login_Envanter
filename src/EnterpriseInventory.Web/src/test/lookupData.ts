import type { LocationItem, LookupItem, ModelItem } from '../inventory/lookupsApi'
import { json } from './mockApi'

export const brands: LookupItem[] = [
  { id: 1, name: 'Dell', isActive: true, rowVersion: 'AAAAAAAAA01=' },
  { id: 2, name: 'Eski Marka', isActive: false, rowVersion: 'AAAAAAAAA02=' },
  { id: 3, name: 'HP', isActive: true, rowVersion: 'AAAAAAAAA03=' },
]

export const models: ModelItem[] = [
  { id: 11, name: 'Latitude 5440', isActive: true, brandId: 1, brandName: 'Dell', rowVersion: 'AAAAAAAAA04=' },
  { id: 12, name: 'Optiplex 7010', isActive: true, brandId: 1, brandName: 'Dell', rowVersion: 'AAAAAAAAA05=' },
  { id: 31, name: 'EliteBook 840', isActive: true, brandId: 3, brandName: 'HP', rowVersion: 'AAAAAAAAA06=' },
]

export const cities: LookupItem[] = [
  { id: 5, name: 'Ankara', isActive: true, rowVersion: 'AAAAAAAAA07=' },
  { id: 6, name: 'İstanbul', isActive: true, rowVersion: 'AAAAAAAAA08=' },
]

export const locations: LocationItem[] = [
  { id: 60, name: 'Merkez Ofis', isActive: true, cityId: 6, cityName: 'İstanbul', rowVersion: 'AAAAAAAAA09=' },
  { id: 61, name: 'Depo', isActive: false, cityId: 6, cityName: 'İstanbul', rowVersion: 'AAAAAAAAA10=' },
]

export const departments: LookupItem[] = [
  { id: 8, name: 'Bilgi İşlem', isActive: true, rowVersion: 'AAAAAAAAA11=' },
  { id: 9, name: 'Muhasebe', isActive: true, rowVersion: 'AAAAAAAAA12=' },
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
