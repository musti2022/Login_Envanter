import {
  Box,
  Button,
  Card,
  LinearProgress,
  Link,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TablePagination,
  TableRow,
  Typography,
} from '@mui/material'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link as RouterLink, useSearchParams } from 'react-router'
import { ExportButton } from '../components/ExportButton'
import { EmptyState } from '../components/states/EmptyState'
import { ErrorState } from '../components/states/ErrorState'
import { LoadingState } from '../components/states/LoadingState'
import { formatDateTime, formatNumber, typeLabels } from '../inventory/labels'
import { pageSizes } from '../inventory/listParams'
import { MovementFilters } from './MovementFilters'
import { activeMovementFilterCount, clearedMovementFilters, parseMovementParams } from './reportParams'
import {
  exportAssignmentReport,
  fetchAssignmentReport,
  movementLabels,
  movementQueryString,
  reportsQueryKey,
  type AssignmentReport,
  type MovementParams,
} from './reportsApi'

/** "Zimmet hareketleri": who was given or gave back which asset, when, and who recorded it; newest first. */
export function AssignmentMovementsReport() {
  const [searchParams, setSearchParams] = useSearchParams()
  const params = parseMovementParams(searchParams)

  const report = useQuery({
    queryKey: [...reportsQueryKey, 'movements', params],
    queryFn: ({ signal }) => fetchAssignmentReport(params, signal),
    placeholderData: keepPreviousData,
  })

  // Built on the address as it is when applied, so a change made while another is pending never undoes it.
  const update = (changes: Partial<MovementParams>, options?: { replace?: boolean }) =>
    setSearchParams((current) => movementQueryString({ ...parseMovementParams(current), ...changes }), options)

  return (
    <Card>
      <MovementFilters params={params} onChange={update} />
      <Box sx={{ height: 4 }}>{report.isFetching && !report.isPending && <LinearProgress aria-label="Rapor yenileniyor" />}</Box>
      {report.isPending ? (
        <LoadingState message="Rapor hazırlanıyor..." />
      ) : report.isError ? (
        <Box sx={{ p: 2 }}>
          <ErrorState
            title="Rapor alınamadı"
            message="Zimmet hareketleri alınamadı. Bağlantınızı kontrol edip tekrar deneyin."
            onRetry={() => void report.refetch()}
          />
        </Box>
      ) : report.data.totalCount === 0 && activeMovementFilterCount(params) > 0 ? (
        <EmptyState
          title="Bu dönemde veya filtrelerle eşleşen hareket yok"
          description="Tarihleri veya filtreleri değiştirip tekrar deneyin."
          action={
            <Button variant="contained" onClick={() => update(clearedMovementFilters)}>
              Filtreleri temizle
            </Button>
          }
        />
      ) : report.data.totalCount === 0 ? (
        <EmptyState title="Henüz zimmet hareketi yok" description="Verilen zimmetler ve alınan iadeler burada listelenir." />
      ) : (
        <>
          <Box sx={{ p: 2, display: 'flex', gap: 2, flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between' }}>
            <Typography role="status" variant="body2">
              {describeTotals(report.data, params)}
            </Typography>
            <ExportButton download={() => exportAssignmentReport(params)} />
          </Box>
          {report.data.items.length === 0 ? (
            <EmptyState
              title="Bu sayfada hareket yok"
              description="Liste değişmiş olabilir."
              action={
                <Button variant="contained" onClick={() => update({ page: 1 })}>
                  İlk sayfaya dön
                </Button>
              }
            />
          ) : (
            <MovementTable
              report={report.data}
              onPage={(page) => update({ page })}
              onPageSize={(pageSize) => update({ pageSize, page: 1 })}
            />
          )}
        </>
      )}
    </Card>
  )
}

/** E.g. "1–31 Ekim 2026: 4 zimmet verildi, 2 iade alındı." */
function describeTotals(report: AssignmentReport, params: MovementParams) {
  const day = (value: string) => {
    const [year, month, date] = value.split('-').map(Number)
    return new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'long', year: 'numeric' }).format(
      new Date(year, month - 1, date),
    )
  }
  const period =
    params.from && params.to
      ? `${day(params.from)} – ${day(params.to)}`
      : params.from
        ? `${day(params.from)} ve sonrası`
        : params.to
          ? `${day(params.to)} ve öncesi`
          : 'Tüm zamanlar'
  const parts = [
    params.movement !== 'Returned' && `${formatNumber(report.assignedCount)} zimmet verildi`,
    params.movement !== 'Assigned' && `${formatNumber(report.returnedCount)} iade alındı`,
  ].filter(Boolean)
  return `${period}: ${parts.join(', ')}.`
}

interface MovementTableProps {
  report: AssignmentReport
  onPage: (page: number) => void
  onPageSize: (pageSize: number) => void
}

function MovementTable({ report, onPage, onPageSize }: MovementTableProps) {
  return (
    <>
      <TableContainer>
        <Table size="small" aria-label="Zimmet hareketleri" sx={{ minWidth: 960, '& td': { verticalAlign: 'top' } }}>
          <TableHead>
            <TableRow>
              {['Tarih', 'Hareket', 'Demirbaş', 'Marka / Model', 'Çalışan', 'Şehir / Departman', 'İşlemi yapan'].map((label) => (
                <TableCell key={label} sx={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
                  {label}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {report.items.map((m) => (
              <TableRow key={`${m.assignmentId}-${m.movement}`} hover>
                <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatDateTime(m.at)}</TableCell>
                <TableCell sx={{ whiteSpace: 'nowrap' }}>{movementLabels[m.movement]}</TableCell>
                <TableCell sx={{ whiteSpace: 'nowrap' }}>
                  <Link component={RouterLink} to={`/envanter/${m.assetId}`} sx={{ fontWeight: 600 }}>
                    {m.assetCode}
                  </Link>
                  <Typography variant="body2" color="textSecondary">
                    {typeLabels[m.assetType] ?? m.assetType}
                    {m.assetArchived && ' · arşivlendi'}
                  </Typography>
                </TableCell>
                <TableCell sx={{ minWidth: 140, overflowWrap: 'anywhere' }}>
                  {m.brandName} {m.modelName}
                  {m.serialNumber && (
                    <Typography variant="body2" color="textSecondary">
                      {m.serialNumber}
                    </Typography>
                  )}
                </TableCell>
                <TableCell sx={{ minWidth: 160, overflowWrap: 'anywhere' }}>
                  {m.employeeDisplayName}
                  <Typography variant="body2" color="textSecondary">
                    {m.employeeUserName}
                  </Typography>
                </TableCell>
                <TableCell sx={{ minWidth: 140 }}>
                  {m.cityName}
                  <Typography variant="body2" color="textSecondary">
                    {m.departmentName}
                  </Typography>
                </TableCell>
                <TableCell sx={{ whiteSpace: 'nowrap' }}>{m.by}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      <TablePagination
        component="div"
        count={report.totalCount}
        page={report.page - 1}
        rowsPerPage={report.pageSize}
        rowsPerPageOptions={[...pageSizes]}
        onPageChange={(_, zeroBased) => onPage(zeroBased + 1)}
        onRowsPerPageChange={(event) => onPageSize(Number(event.target.value))}
        labelRowsPerPage="Sayfa başına hareket:"
        labelDisplayedRows={({ from, to, count }) => `${from}–${to} / ${count}`}
        getItemAriaLabel={(type) =>
          ({ first: 'İlk sayfa', last: 'Son sayfa', next: 'Sonraki sayfa', previous: 'Önceki sayfa' })[type]
        }
        showFirstButton
        showLastButton
        sx={{
          '& .MuiTablePagination-toolbar': { flexWrap: 'wrap', justifyContent: 'flex-end', rowGap: 0.5, px: { xs: 1, sm: 2 } },
          '& .MuiTablePagination-spacer': { display: { xs: 'none', sm: 'block' } },
          '& .MuiTablePagination-actions': { ml: { xs: 1, sm: 2.5 } },
        }}
      />
      <Typography variant="body2" color="textSecondary" sx={{ px: 2, pb: 2 }}>
        Şehir ve departman, demirbaşın bugünkü yeridir. Saatler bu bilgisayarın saat dilimindedir.
      </Typography>
    </>
  )
}
