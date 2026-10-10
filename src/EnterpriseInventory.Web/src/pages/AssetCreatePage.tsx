import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { PageHeader } from '../components/PageHeader'
import { assetSaved } from '../inventory/assetCache'
import { AssetForm } from '../inventory/AssetForm'
import { createAsset } from '../inventory/assetsApi'

export function AssetCreatePage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  return (
    <>
      <PageHeader title="Yeni Demirbaş" description="Envantere yeni bir demirbaş ekleyin. Yıldızlı alanlar zorunludur." />
      <AssetForm
        submitLabel="Demirbaşı ekle"
        onCancel={() => navigate('/envanter')}
        onSubmit={async (body) => {
          const created = await createAsset(body)
          assetSaved(queryClient, created)
          navigate(`/envanter/${created.id}`, { state: { notice: `${created.assetCode} envantere eklendi.` } })
        }}
      />
    </>
  )
}
