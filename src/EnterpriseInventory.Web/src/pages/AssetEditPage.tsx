import { Alert, Button } from '@mui/material'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link as RouterLink, useNavigate, useParams } from 'react-router'
import { ApiError } from '../api/http'
import { PageHeader } from '../components/PageHeader'
import { ErrorState } from '../components/states/ErrorState'
import { LoadingState } from '../components/states/LoadingState'
import { assetSaved } from '../inventory/assetCache'
import { AssetForm } from '../inventory/AssetForm'
import { AssetNotFound } from '../inventory/AssetNotFound'
import { assetQueryKey, fetchAsset, retryUnlessNotFound, updateAsset, type AssetDetails } from '../inventory/assetsApi'
import { parseAssetId } from '../inventory/assetId'

export function AssetEditPage() {
  const assetId = parseAssetId(useParams().id)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const asset = useQuery({
    queryKey: assetQueryKey(assetId ?? 0),
    queryFn: ({ signal }) => fetchAsset(assetId!, signal),
    enabled: assetId !== null,
    retry: retryUnlessNotFound,
    // A refetch must not move the edit onto a newer version: see base below.
    refetchOnWindowFocus: false,
  })

  // The version the edit started from. Its row version goes with the save, so a change someone else made in
  // the meantime is refused (409) instead of overwritten, even if this page fetched the asset again.
  const [base, setBase] = useState<AssetDetails | null>(null)
  const [reloaded, setReloaded] = useState(false)
  const current = base?.id === assetId ? base : null
  if (!current && asset.data && asset.data.id === assetId) {
    setBase(asset.data)
  }

  const detailPath = `/envanter/${assetId}`
  const header = <PageHeader title="Demirbaşı Düzenle" description={current ? current.assetCode : undefined} />

  if (assetId === null || (asset.error instanceof ApiError && asset.error.status === 404)) {
    return (
      <>
        {header}
        <AssetNotFound />
      </>
    )
  }

  if (asset.isError && !current) {
    return (
      <>
        {header}
        <ErrorState title="Demirbaş yüklenemedi" message="Bağlantınızı kontrol edip tekrar deneyin." onRetry={() => void asset.refetch()} />
      </>
    )
  }

  if (!current) {
    return (
      <>
        {header}
        <LoadingState message="Demirbaş yükleniyor..." />
      </>
    )
  }

  if (current.isArchived) {
    return (
      <>
        {header}
        <Alert
          severity="info"
          action={
            <Button color="inherit" size="small" component={RouterLink} to={detailPath}>
              Detaya git
            </Button>
          }
        >
          Arşivlenmiş demirbaş düzenlenemez; yalnızca görüntülenebilir.
        </Alert>
      </>
    )
  }

  // A live notification refetched the asset and someone else has saved it since this edit started.
  const newer = asset.data && asset.data.id === current.id && asset.data.rowVersion !== current.rowVersion ? asset.data : null
  const loadNewer = (version: AssetDetails) => {
    setBase(version)
    setReloaded(true)
  }

  return (
    <>
      {header}
      {newer && (
        <Alert
          severity="warning"
          sx={{ mb: 2 }}
          action={
            <Button color="inherit" size="small" onClick={() => loadNewer(newer)}>
              Güncel kaydı yükle
            </Button>
          }
        >
          Bu demirbaş siz düzenlerken başka bir kullanıcı tarafından değiştirildi. Şimdi kaydederseniz kayıt çakışması
          uyarısı alırsınız.
        </Alert>
      )}
      {reloaded && (
        <Alert severity="info" sx={{ mb: 2 }} onClose={() => setReloaded(false)}>
          Kaydın güncel hali yüklendi. Değişikliklerinizi yeniden yapıp kaydedin.
        </Alert>
      )}
      <AssetForm
        // A new version starts a new form, with its values.
        key={current.rowVersion}
        asset={current}
        submitLabel="Kaydet"
        onCancel={() => navigate(detailPath)}
        onSubmit={async (body) => {
          const saved = await updateAsset(current.id, body, current.rowVersion)
          assetSaved(queryClient, saved)
          navigate(detailPath, { state: { notice: 'Değişiklikler kaydedildi.' } })
        }}
        onReload={async () => {
          const fresh = await asset.refetch()
          if (fresh.data) {
            loadNewer(fresh.data)
          }
        }}
      />
    </>
  )
}
