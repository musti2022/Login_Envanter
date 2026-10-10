import { Alert, Box } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { PageHeader } from '../components/PageHeader'
import { LookupSection } from '../definitions/LookupSection'
import { allModelsQuery, brandsQuery, type ModelItem } from '../inventory/lookupsApi'

/** "Marka ve Modeller": brands and their models; add, rename, deactivate and reactivate. */
export function BrandsModelsPage() {
  const brands = useQuery(brandsQuery)
  const models = useQuery(allModelsQuery)

  return (
    <>
      <PageHeader title="Marka ve Modeller" description="Demirbaş marka ve model tanımlarını ekleyin, adlarını değiştirin, pasifleştirin." />
      <Alert severity="info" sx={{ mb: 3 }}>
        Tanımlar silinmez; demirbaşlar ve geçmiş onlara bağlıdır. Artık kullanılmayacak bir tanımı pasifleştirin: yeni
        kayıtlarda seçilemez, onu kullanan demirbaşlar değişmez.
      </Alert>
      <Box sx={{ display: 'grid', gap: 3, gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: 'minmax(0, 2fr) minmax(0, 3fr)' } }}>
        <LookupSection title="Markalar" noun="marka" path="/api/brands" query={brands} />
        <LookupSection<ModelItem>
          title="Modeller"
          noun="model"
          path="/api/models"
          query={models}
          parent={{
            label: 'Marka',
            possessive: 'markasının',
            field: 'brandId',
            options: brands.data ?? [],
            idOf: (model) => model.brandId,
            nameOf: (model) => model.brandName,
          }}
        />
      </Box>
    </>
  )
}
