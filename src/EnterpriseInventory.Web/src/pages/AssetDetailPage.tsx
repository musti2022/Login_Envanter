import ArchiveOutlinedIcon from '@mui/icons-material/ArchiveOutlined'
import ArrowBackIcon from '@mui/icons-material/ArrowBack'
import AssignmentIndOutlinedIcon from '@mui/icons-material/AssignmentIndOutlined'
import AssignmentReturnOutlinedIcon from '@mui/icons-material/AssignmentReturnOutlined'
import EditIcon from '@mui/icons-material/Edit'
import { Alert, Box, Button, Card, CardContent, CardHeader, Chip, Tooltip, Typography } from '@mui/material'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { Link as RouterLink, useLocation, useNavigate, useParams } from 'react-router'
import { ApiError } from '../api/http'
import { PageHeader } from '../components/PageHeader'
import { ErrorState } from '../components/states/ErrorState'
import { LoadingState } from '../components/states/LoadingState'
import { ArchiveAssetDialog } from '../inventory/ArchiveAssetDialog'
import { assetSaved } from '../inventory/assetCache'
import { AssetHistory } from '../inventory/AssetHistory'
import { AssignAssetDialog } from '../inventory/AssignAssetDialog'
import { AssignmentHistory } from '../inventory/AssignmentHistory'
import { parseAssetId } from '../inventory/assetId'
import { AssetNotFound } from '../inventory/AssetNotFound'
import { assetQueryKey, assetsQueryKey, fetchAsset, retryUnlessNotFound, type AssetDetails } from '../inventory/assetsApi'
import { dashboardQueryKey } from '../dashboard/dashboardApi'
import { formatDateTime, statusLabels, typeLabels } from '../inventory/labels'
import { ReturnAssetDialog } from '../inventory/ReturnAssetDialog'
import { StatusChip } from '../inventory/StatusChip'

export function AssetDetailPage() {
  const assetId = parseAssetId(useParams().id)
  const location = useLocation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [archiveTarget, setArchiveTarget] = useState<AssetDetails | null>(null)
  const [assignTarget, setAssignTarget] = useState<AssetDetails | null>(null)
  const [returnTarget, setReturnTarget] = useState<AssetDetails | null>(null)
  const asset = useQuery({
    queryKey: assetQueryKey(assetId ?? 0),
    queryFn: ({ signal }) => fetchAsset(assetId!, signal),
    enabled: assetId !== null,
    retry: retryUnlessNotFound,
  })

  // A message the previous page left (saved, added); cleared from the history entry once read.
  const notice = (location.state as { notice?: string } | null)?.notice
  const dismissNotice = () => navigate(location.pathname, { replace: true, state: null })

  const back = (
    <Button startIcon={<ArrowBackIcon />} component={RouterLink} to="/envanter" sx={{ mb: 1 }}>
      Envanter
    </Button>
  )

  if (assetId === null || (asset.error instanceof ApiError && asset.error.status === 404)) {
    return (
      <>
        {back}
        <PageHeader title="Demirbaş" />
        <AssetNotFound />
      </>
    )
  }

  if (asset.isPending) {
    return (
      <>
        {back}
        <LoadingState message="Demirbaş yükleniyor..." />
      </>
    )
  }

  if (asset.isError) {
    return (
      <>
        {back}
        <PageHeader title="Demirbaş" />
        <ErrorState title="Demirbaş yüklenemedi" message="Bağlantınızı kontrol edip tekrar deneyin." onRetry={() => void asset.refetch()} />
      </>
    )
  }

  const data = asset.data
  const assigned = data.activeAssignment !== null
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: assetsQueryKey })
    void queryClient.invalidateQueries({ queryKey: dashboardQueryKey })
  }
  const saved = (asset: AssetDetails, notice: string) => {
    assetSaved(queryClient, asset)
    navigate(location.pathname, { replace: true, state: { notice } })
  }
  const assignable = data.status === 'Available'

  return (
    <>
      {back}
      <PageHeader
        title={data.assetCode}
        description={`${typeLabels[data.assetType]} · ${data.brand.name} ${data.model.name}`}
        actions={
          !data.isArchived && (
            <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
              {assigned ? (
                <Button variant="contained" startIcon={<AssignmentReturnOutlinedIcon />} onClick={() => setReturnTarget(data)}>
                  İade Al
                </Button>
              ) : (
                <Tooltip title={assignable ? '' : `${statusLabels[data.status]} demirbaş zimmetlenemez; önce durumunu Boşta yapın.`}>
                  <span>
                    <Button variant="contained" startIcon={<AssignmentIndOutlinedIcon />} disabled={!assignable} onClick={() => setAssignTarget(data)}>
                      Zimmet Ver
                    </Button>
                  </span>
                </Tooltip>
              )}
              <Button variant="outlined" startIcon={<EditIcon />} component={RouterLink} to={`/envanter/${data.id}/duzenle`}>
                Düzenle
              </Button>
              <Tooltip title={assigned ? 'Zimmetli demirbaş arşivlenemez; önce iadesini alın.' : ''}>
                <span>
                  <Button color="error" variant="outlined" startIcon={<ArchiveOutlinedIcon />} disabled={assigned} onClick={() => setArchiveTarget(data)}>
                    Arşivle
                  </Button>
                </span>
              </Tooltip>
            </Box>
          )
        }
      />
      {notice && (
        <Alert severity="success" sx={{ mb: 2 }} onClose={dismissNotice}>
          {notice}
        </Alert>
      )}
      {data.isArchived && (
        <Alert severity="info" sx={{ mb: 2 }}>
          Bu demirbaş arşivlenmiş; yalnızca görüntülenebilir.
        </Alert>
      )}
      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: 'repeat(2, minmax(0, 1fr))' } }}>
        <Section title="Demirbaş bilgileri">
          <Field label="Durum">
            <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
              <StatusChip status={data.status} />
              {data.isArchived && <Chip size="small" label="Arşivlenmiş" />}
            </Box>
          </Field>
          <Field label="Demirbaş Kodu">{data.assetCode}</Field>
          <Field label="Tür">{typeLabels[data.assetType]}</Field>
          <Field label="Bilgisayar Adı">{data.computerName}</Field>
          <Field label="Seri No">{data.serialNumber}</Field>
          <Field label="Marka">{data.brand.name}</Field>
          <Field label="Model">{data.model.name}</Field>
          <Field label="Açıklama" wide>
            {data.description}
          </Field>
        </Section>
        <Box sx={{ display: 'grid', gap: 2, alignContent: 'start' }}>
          <Section title="Konum">
            <Field label="Şehir">{data.city.name}</Field>
            <Field label="Lokasyon">{data.location?.name}</Field>
            <Field label="Departman">{data.department.name}</Field>
          </Section>
          <Section title="Zimmet">
            {data.activeAssignment ? (
              <>
                <Field label="Zimmetli Kişi">
                  {data.activeAssignment.displayName} ({data.activeAssignment.userName})
                </Field>
                <Field label="Zimmet Tanımı">{data.activeAssignment.assignmentDescription}</Field>
                <Field label="Zimmet Tarihi">{formatDateTime(data.activeAssignment.assignedAt)}</Field>
                <Field label="Zimmetleyen">{data.activeAssignment.assignedBy}</Field>
              </>
            ) : (
              <Typography color="textSecondary" sx={{ gridColumn: '1 / -1' }}>
                Bu demirbaş kimseye zimmetli değil.
              </Typography>
            )}
          </Section>
          <Section title="Kayıt bilgileri">
            <Field label="Oluşturan">
              {data.createdBy} · {formatDateTime(data.createdAt)}
            </Field>
            <Field label="Son Değiştiren">{data.updatedAt && `${data.updatedBy} · ${formatDateTime(data.updatedAt)}`}</Field>
          </Section>
        </Box>
      </Box>
      <Box sx={{ mt: 2, display: 'grid', gap: 2, gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: 'repeat(2, minmax(0, 1fr))' }, alignItems: 'start' }}>
        <Card>
          <CardHeader title="Zimmet geçmişi" slotProps={{ title: { variant: 'h6', component: 'h2' } }} />
          <CardContent sx={{ pt: 0 }}>
            <AssignmentHistory assetId={data.id} />
          </CardContent>
        </Card>
        <Card>
          <CardHeader title="Geçmiş" slotProps={{ title: { variant: 'h6', component: 'h2' } }} />
          <CardContent sx={{ pt: 0 }}>
            <AssetHistory assetId={data.id} />
          </CardContent>
        </Card>
      </Box>
      <AssignAssetDialog
        asset={assignTarget}
        onClose={() => setAssignTarget(null)}
        onAssigned={(asset) => {
          setAssignTarget(null)
          const holder = asset.activeAssignment
          saved(asset, holder ? `${asset.assetCode}, ${holder.displayName} adlı çalışana zimmetlendi.` : `${asset.assetCode} zimmetlendi.`)
        }}
        onConflict={refresh}
      />
      <ReturnAssetDialog
        asset={returnTarget}
        onClose={() => setReturnTarget(null)}
        onReturned={(asset) => {
          setReturnTarget(null)
          saved(asset, `${asset.assetCode} iade alındı.`)
        }}
        onConflict={refresh}
      />
      <ArchiveAssetDialog
        asset={archiveTarget}
        onClose={() => setArchiveTarget(null)}
        onArchived={() => {
          setArchiveTarget(null)
          refresh()
          navigate(location.pathname, { replace: true, state: { notice: `${data.assetCode} arşivlendi.` } })
        }}
        onConflict={refresh}
      />
    </>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Card>
      <CardHeader title={title} slotProps={{ title: { variant: 'h6', component: 'h2' } }} />
      <CardContent sx={{ pt: 0 }}>
        <Box component="dl" sx={{ m: 0, display: 'grid', gap: 2, gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: 'repeat(2, minmax(0, 1fr))' } }}>
          {children}
        </Box>
      </CardContent>
    </Card>
  )
}

/** A label and its value; an empty value is written as a dash, read as "Boş". */
function Field({ label, wide, children }: { label: string; wide?: boolean; children: ReactNode }) {
  const empty = children === null || children === undefined || children === '' || children === false
  return (
    <Box sx={{ gridColumn: wide ? '1 / -1' : undefined, minWidth: 0 }}>
      <Typography component="dt" variant="body2" color="textSecondary">
        {label}
      </Typography>
      <Typography component="dd" sx={{ m: 0, overflowWrap: 'anywhere', whiteSpace: 'pre-line' }}>
        {empty ? <span aria-label="Boş">—</span> : children}
      </Typography>
    </Box>
  )
}
