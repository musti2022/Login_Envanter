import AddIcon from '@mui/icons-material/Add'
import TimelineIcon from '@mui/icons-material/Timeline'
import { Box, Button, Card, LinearProgress } from '@mui/material'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link as RouterLink, useSearchParams } from 'react-router'
import { usePreferences } from '../app/preferencesContext'
import { ExportButton } from '../components/ExportButton'
import { PageHeader } from '../components/PageHeader'
import { EmptyState } from '../components/states/EmptyState'
import { ErrorState } from '../components/states/ErrorState'
import { LoadingState } from '../components/states/LoadingState'
import { AssetFilters } from '../inventory/AssetFilters'
import { AssetTable } from '../inventory/AssetTable'
import {
  assetsQueryKey,
  exportAssets,
  fetchAssets,
  toQueryString,
  type AssetListParams,
  type SortField,
} from '../inventory/assetsApi'
import { ColumnMenu } from '../inventory/ColumnMenu'
import { activeFilterCount, clearedFilters, parseListParams } from '../inventory/listParams'

interface InventoryPageProps {
  /**
   * "Zimmetler": only the assets assigned now, with the employee they are assigned to. Assigning and taking back
   * happen on the asset's page, where the row leads.
   */
  assignedOnly?: boolean
}

export function InventoryPage({ assignedOnly = false }: InventoryPageProps) {
  const [searchParams, setSearchParams] = useSearchParams()
  // On "Zimmetler" the state is fixed: the filters do not offer it and the request always carries it.
  const filters: AssetListParams = assignedOnly
    ? { ...parseListParams(searchParams), status: [], archived: false }
    : parseListParams(searchParams)
  const params: AssetListParams = assignedOnly ? { ...filters, status: ['Assigned'] } : filters
  const { hiddenInventoryColumns: hiddenColumns, setHiddenInventoryColumns: setHiddenColumns } = usePreferences()

  const assets = useQuery({
    queryKey: [...assetsQueryKey, 'list', params],
    queryFn: ({ signal }) => fetchAssets(params, signal),
    placeholderData: keepPreviousData,
  })

  // Built on the address as it is when applied, so a change made while another is pending (the search waits
  // for typing to stop) never undoes it.
  const update = (changes: Partial<AssetListParams>, options?: { replace?: boolean }) =>
    setSearchParams((current) => toQueryString({ ...parseListParams(current), ...changes }), options)

  const sort = (sortBy: SortField) =>
    update({
      sortBy,
      sortDirection: params.sortBy === sortBy && params.sortDirection === 'asc' ? 'desc' : 'asc',
      page: 1,
    })

  return (
    <>
      <PageHeader
        title={assignedOnly ? 'Zimmetler' : 'Envanter'}
        description={
          assignedOnly
            ? 'Şu an zimmetli demirbaşlar. Zimmet vermek veya iade almak için demirbaşın sayfasını açın.'
            : 'Demirbaşları listeleyin, arayın ve yönetin.'
        }
        actions={
          <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
            <ColumnMenu hidden={hiddenColumns} onChange={setHiddenColumns} />
            <ExportButton download={() => exportAssets(params)} disabled={assets.data?.totalCount === 0} />
            {assignedOnly ? (
              <Button variant="outlined" startIcon={<TimelineIcon />} component={RouterLink} to="/raporlar/zimmet-hareketleri">
                Zimmet hareketleri
              </Button>
            ) : (
              <Button variant="contained" startIcon={<AddIcon />} component={RouterLink} to="/envanter/yeni">
                Yeni Demirbaş
              </Button>
            )}
          </Box>
        }
      />
      <Card>
        <AssetFilters params={filters} onChange={update} archiveSwitch={!assignedOnly} statusFilter={!assignedOnly} />
        <Box sx={{ height: 4 }}>{assets.isFetching && !assets.isPending && <LinearProgress aria-label="Liste yenileniyor" />}</Box>
        {assets.isPending ? (
          <LoadingState message="Demirbaşlar yükleniyor..." />
        ) : assets.isError ? (
          <Box sx={{ p: 2 }}>
            <ErrorState
              title="Demirbaşlar yüklenemedi"
              message="Liste alınamadı. Bağlantınızı kontrol edip tekrar deneyin."
              onRetry={() => void assets.refetch()}
            />
          </Box>
        ) : assets.data.totalCount === 0 && activeFilterCount(filters) > 0 ? (
          <EmptyState
            title="Filtrelerle eşleşen demirbaş yok"
            description="Aramayı veya filtreleri değiştirip tekrar deneyin."
            action={
              <Button variant="contained" onClick={() => update(clearedFilters)}>
                Filtreleri temizle
              </Button>
            }
          />
        ) : assets.data.totalCount === 0 && assignedOnly ? (
          <EmptyState
            title="Zimmetli demirbaş yok"
            description="Zimmet, demirbaşın sayfasındaki Zimmet Ver ile verilir."
            action={
              <Button variant="contained" component={RouterLink} to="/envanter">
                Envantere git
              </Button>
            }
          />
        ) : assets.data.totalCount === 0 ? (
          <EmptyState title="Henüz demirbaş yok" description="Envantere eklenen demirbaşlar burada listelenir." />
        ) : assets.data.items.length === 0 ? (
          <EmptyState
            title="Bu sayfada kayıt yok"
            description="Liste değişmiş olabilir."
            action={
              <Button variant="contained" onClick={() => update({ page: 1 })}>
                İlk sayfaya dön
              </Button>
            }
          />
        ) : (
          <AssetTable
            page={assets.data}
            params={params}
            hiddenColumns={hiddenColumns}
            onSort={sort}
            onPage={(page) => update({ page })}
            onPageSize={(pageSize) => update({ pageSize, page: 1 })}
          />
        )}
      </Card>
    </>
  )
}
