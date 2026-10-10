import { Box, Button, Card, LinearProgress } from '@mui/material'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { AuditEntryDialog } from '../audit/AuditEntryDialog'
import { AuditFilters } from '../audit/AuditFilters'
import { AuditTable } from '../audit/AuditTable'
import { auditLogsQueryKey, fetchAuditLogs, toPageQuery, type AuditLogEntry, type AuditLogParams } from '../audit/auditApi'
import { activeAuditFilterCount, clearedAuditFilters, parseAuditParams } from '../audit/auditParams'
import { PageHeader } from '../components/PageHeader'
import { EmptyState } from '../components/states/EmptyState'
import { ErrorState } from '../components/states/ErrorState'
import { LoadingState } from '../components/states/LoadingState'

/** "Denetim Geçmişi": every audit record, filtered and paged on the server, with each change's old and new values. */
export function AuditLogPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const params = parseAuditParams(searchParams)
  const [selected, setSelected] = useState<AuditLogEntry | null>(null)

  const logs = useQuery({
    queryKey: [...auditLogsQueryKey, 'list', params],
    queryFn: ({ signal }) => fetchAuditLogs(params, signal),
    placeholderData: keepPreviousData,
  })

  // Built on the address as it is when applied, so a change made while another is pending never undoes it.
  const update = (changes: Partial<AuditLogParams>) =>
    setSearchParams((current) => toPageQuery({ ...parseAuditParams(current), ...changes }))

  return (
    <>
      <PageHeader title="Denetim Geçmişi" description="Kayıtlarda kimin, ne zaman, neyi neyken neye değiştirdiğini izleyin." />
      <Card>
        <AuditFilters params={params} onChange={update} />
        <Box sx={{ height: 4 }}>{logs.isFetching && !logs.isPending && <LinearProgress aria-label="Kayıtlar yenileniyor" />}</Box>
        {logs.isPending ? (
          <LoadingState message="Denetim kayıtları yükleniyor..." />
        ) : logs.isError ? (
          <Box sx={{ p: 2 }}>
            <ErrorState
              title="Denetim kayıtları yüklenemedi"
              message="Kayıtlar alınamadı. Bağlantınızı kontrol edip tekrar deneyin."
              onRetry={() => void logs.refetch()}
            />
          </Box>
        ) : logs.data.totalCount === 0 && activeAuditFilterCount(params) > 0 ? (
          <EmptyState
            title="Filtrelerle eşleşen kayıt yok"
            description="Filtreleri değiştirip tekrar deneyin."
            action={
              <Button variant="contained" onClick={() => update(clearedAuditFilters)}>
                Filtreleri temizle
              </Button>
            }
          />
        ) : logs.data.totalCount === 0 ? (
          <EmptyState title="Henüz denetim kaydı yok" description="Kayıtlarda yapılan her değişiklik burada listelenir." />
        ) : logs.data.items.length === 0 ? (
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
          <AuditTable
            page={logs.data}
            onOpen={setSelected}
            onPage={(page) => update({ page })}
            onPageSize={(pageSize) => update({ pageSize, page: 1 })}
          />
        )}
      </Card>
      <AuditEntryDialog
        entry={selected}
        onClose={() => setSelected(null)}
        onShowRequest={(correlationId) => update({ ...clearedAuditFilters, correlationId })}
      />
    </>
  )
}
