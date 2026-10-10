import {
  Box,
  Button,
  Card,
  LinearProgress,
  Link,
  MenuItem,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableFooter,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link as RouterLink, useSearchParams } from 'react-router'
import { ExportButton } from '../components/ExportButton'
import { EmptyState } from '../components/states/EmptyState'
import { ErrorState } from '../components/states/ErrorState'
import { LoadingState } from '../components/states/LoadingState'
import { AssetFilters } from '../inventory/AssetFilters'
import type { AssetListParams } from '../inventory/assetsApi'
import { formatNumber } from '../inventory/labels'
import { activeFilterCount, clearedFilters } from '../inventory/listParams'
import { parseSummaryParams } from './reportParams'
import {
  assetGroupings,
  exportAssetSummary,
  fetchAssetSummary,
  groupingLabels,
  inventoryLink,
  reportsQueryKey,
  summaryQueryString,
  type AssetGrouping,
  type AssetSummary,
  type AssetSummaryRow,
  type SummaryParams,
} from './reportsApi'

const countColumns = [
  { key: 'totalCount', label: 'Toplam' },
  { key: 'assignedCount', label: 'Zimmetli' },
  { key: 'availableCount', label: 'Boşta' },
  { key: 'faultyCount', label: 'Arızalı' },
  { key: 'retiredCount', label: 'Hurda' },
] as const

/**
 * "Envanter özeti": assets that are not archived, counted by status per city, department, location, brand, model,
 * type or status, with the inventory list's filters. Every group opens the inventory list of its assets.
 */
export function AssetSummaryReport() {
  const [searchParams, setSearchParams] = useSearchParams()
  const params = parseSummaryParams(searchParams)

  const summary = useQuery({
    queryKey: [...reportsQueryKey, 'summary', params],
    queryFn: ({ signal }) => fetchAssetSummary(params, signal),
    placeholderData: keepPreviousData,
  })

  // Built on the address as it is when applied, so a change made while another is pending never undoes it.
  const update = (change: (current: SummaryParams) => SummaryParams, options?: { replace?: boolean }) =>
    setSearchParams((current) => summaryQueryString(change(parseSummaryParams(current))), options)
  const setFilters = (changes: Partial<AssetListParams>, options?: { replace?: boolean }) =>
    update((current) => ({ ...current, filters: { ...current.filters, ...changes } }), options)

  return (
    <Card>
      <Box sx={{ p: 2, pb: 0, display: 'flex', gap: 2, flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between' }}>
        <TextField
          select
          size="small"
          label="Gruplama"
          value={params.groupBy}
          onChange={(event) => update((current) => ({ ...current, groupBy: event.target.value as AssetGrouping }))}
          sx={{ minWidth: 200 }}
          slotProps={{ inputLabel: { shrink: true } }}
        >
          {assetGroupings.map((grouping) => (
            <MenuItem key={grouping} value={grouping}>
              {groupingLabels[grouping]}
            </MenuItem>
          ))}
        </TextField>
        <ExportButton download={() => exportAssetSummary(params)} disabled={summary.data?.total.totalCount === 0} />
      </Box>
      <AssetFilters params={params.filters} onChange={setFilters} archiveSwitch={false} />
      <Box sx={{ height: 4 }}>
        {summary.isFetching && !summary.isPending && <LinearProgress aria-label="Rapor yenileniyor" />}
      </Box>
      {summary.isPending ? (
        <LoadingState message="Rapor hazırlanıyor..." />
      ) : summary.isError ? (
        <Box sx={{ p: 2 }}>
          <ErrorState
            title="Rapor alınamadı"
            message="Rapor verileri alınamadı. Bağlantınızı kontrol edip tekrar deneyin."
            onRetry={() => void summary.refetch()}
          />
        </Box>
      ) : summary.data.total.totalCount === 0 && activeFilterCount(params.filters) > 0 ? (
        <EmptyState
          title="Filtrelerle eşleşen demirbaş yok"
          description="Aramayı veya filtreleri değiştirip tekrar deneyin."
          action={
            <Button variant="contained" onClick={() => setFilters(clearedFilters)}>
              Filtreleri temizle
            </Button>
          }
        />
      ) : summary.data.total.totalCount === 0 ? (
        <EmptyState title="Henüz demirbaş yok" description="Envantere eklenen demirbaşlar bu raporda sayılır." />
      ) : (
        <SummaryTable summary={summary.data} params={params} />
      )}
    </Card>
  )
}

function SummaryTable({ summary, params }: { summary: AssetSummary; params: SummaryParams }) {
  const label = groupingLabels[params.groupBy]
  const numbers = { fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' } as const

  const cells = (row: AssetSummaryRow) =>
    countColumns.map((column) => (
      <TableCell key={column.key} align="right" sx={numbers}>
        {formatNumber(row[column.key])}
      </TableCell>
    ))

  return (
    <>
      <TableContainer>
        <Table size="small" aria-label={`${label} bazında envanter özeti`} sx={{ minWidth: 560 }}>
          <TableHead>
            <TableRow>
              <TableCell sx={{ fontWeight: 600 }}>{label}</TableCell>
              {countColumns.map((column) => (
                <TableCell key={column.key} align="right" sx={{ fontWeight: 600 }}>
                  {column.label}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {summary.rows.map((row) => (
              <TableRow key={row.key} hover>
                <TableCell sx={{ overflowWrap: 'anywhere' }}>
                  {row.inventoryFilter ? (
                    <Link component={RouterLink} to={inventoryLink(params.filters, row.inventoryFilter)}>
                      {row.name}
                    </Link>
                  ) : (
                    row.name
                  )}
                </TableCell>
                {cells(row)}
              </TableRow>
            ))}
          </TableBody>
          <TableFooter>
            <TableRow sx={{ '& td': { fontWeight: 700, color: 'text.primary', fontSize: '0.875rem' } }}>
              <TableCell>{summary.total.name}</TableCell>
              {cells(summary.total)}
            </TableRow>
          </TableFooter>
        </Table>
      </TableContainer>
      <Typography variant="body2" color="textSecondary" sx={{ p: 2 }}>
        {formatNumber(summary.rows.length)} {label.toLocaleLowerCase('tr-TR')} · Arşivlenmiş demirbaşlar sayılmaz. Bir satıra
        tıklayınca envanter o demirbaşlarla açılır.
      </Typography>
    </>
  )
}
