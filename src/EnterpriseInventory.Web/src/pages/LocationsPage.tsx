import { Alert, Box } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { PageHeader } from '../components/PageHeader'
import { LookupSection } from '../definitions/LookupSection'
import { allLocationsQuery, citiesQuery, departmentsQuery, type LocationItem } from '../inventory/lookupsApi'

/** "Lokasyonlar": cities, the locations in them and departments; add, rename, deactivate and reactivate. */
export function LocationsPage() {
  const cities = useQuery(citiesQuery)
  const locations = useQuery(allLocationsQuery)
  const departments = useQuery(departmentsQuery)

  return (
    <>
      <PageHeader title="Lokasyonlar" description="Şehir, lokasyon ve departman tanımlarını ekleyin, adlarını değiştirin, pasifleştirin." />
      <Alert severity="info" sx={{ mb: 3 }}>
        Tanımlar silinmez; demirbaşlar ve geçmiş onlara bağlıdır. Artık kullanılmayacak bir tanımı pasifleştirin: yeni
        kayıtlarda seçilemez, onu kullanan demirbaşlar değişmez.
      </Alert>
      <Box sx={{ display: 'grid', gap: 3, gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: 'repeat(2, minmax(0, 1fr))' } }}>
        <LookupSection title="Şehirler" noun="şehir" path="/api/cities" query={cities} />
        <LookupSection title="Departmanlar" noun="departman" path="/api/departments" query={departments} />
        <Box sx={{ gridColumn: { lg: '1 / -1' } }}>
          <LookupSection<LocationItem>
            title="Lokasyonlar"
            noun="lokasyon"
            path="/api/locations"
            query={locations}
            parent={{
              label: 'Şehir',
              possessive: 'şehrinin',
              field: 'cityId',
              options: cities.data ?? [],
              idOf: (location) => location.cityId,
              nameOf: (location) => location.cityName,
            }}
          />
        </Box>
      </Box>
    </>
  )
}
