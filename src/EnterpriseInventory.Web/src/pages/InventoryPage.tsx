import { Box, Button, Card, LinearProgress } from '@mui/material'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { usePreferences } from '../app/preferencesContext'
import { PageHeader } from '../components/PageHeader'
import { EmptyState } from '../components/states/EmptyState'
import { ErrorState } from '../components/states/ErrorState'
import { LoadingState } from '../components/states/LoadingState'
import { AssetTable } from '../inventory/AssetTable'
import { assetsQueryKey, fetchAssets, toQueryString, type AssetListParams, type SortField } from '../inventory/assetsApi'
import { ColumnMenu } from '../inventory/ColumnMenu'
import { parseListParams } from '../inventory/listParams'

export function InventoryPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const params = parseListParams(searchParams)
  const { hiddenInventoryColumns: hiddenColumns, setHiddenInventoryColumns: setHiddenColumns } = usePreferences()

  const assets = useQuery({
    queryKey: [...assetsQueryKey, 'list', params],
    queryFn: ({ signal }) => fetchAssets(params, signal),
    placeholderData: keepPreviousData,
  })

  const update = (changes: Partial<AssetListParams>) => setSearchParams(toQueryString({ ...params, ...changes }))

  const sort = (sortBy: SortField) =>
    update({
      sortBy,
      sortDirection: params.sortBy === sortBy && params.sortDirection === 'asc' ? 'desc' : 'asc',
      page: 1,
    })

  return (
    <>
      <PageHeader
        title="Envanter"
        description="Demirbaşları listeleyin, arayın ve yönetin."
        actions={<ColumnMenu hidden={hiddenColumns} onChange={setHiddenColumns} />}
      />
      <Card>
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
