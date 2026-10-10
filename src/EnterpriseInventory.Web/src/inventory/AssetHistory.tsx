import { Box, Button, Divider, LinearProgress, List, ListItem, Typography } from '@mui/material'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { EmptyState } from '../components/states/EmptyState'
import { ErrorState } from '../components/states/ErrorState'
import { LoadingState } from '../components/states/LoadingState'
import { assetHistoryQueryKey, fetchAssetHistory } from './assetsApi'
import { changesOf } from './historyChanges'
import { actionLabel, formatDateTime } from './labels'

/** The asset's audit history, newest first, a page at a time. */
export function AssetHistory({ assetId }: { assetId: number }) {
  const [page, setPage] = useState(1)
  const history = useQuery({
    queryKey: assetHistoryQueryKey(assetId, page),
    queryFn: ({ signal }) => fetchAssetHistory(assetId, page, signal),
    placeholderData: keepPreviousData,
  })

  if (history.isPending) return <LoadingState message="Geçmiş yükleniyor..." />
  if (history.isError) {
    return <ErrorState title="Geçmiş yüklenemedi" message="Bağlantınızı kontrol edip tekrar deneyin." onRetry={() => void history.refetch()} />
  }
  if (history.data.totalCount === 0) {
    return <EmptyState title="Henüz geçmiş kaydı yok" description="Demirbaşta yapılan değişiklikler burada listelenir." />
  }

  const { items, totalPages, totalCount } = history.data
  return (
    <Box>
      <Box sx={{ height: 4 }}>{history.isFetching && <LinearProgress aria-label="Geçmiş yenileniyor" />}</Box>
      <List aria-label="Demirbaş geçmişi" disablePadding>
        {items.map((entry, index) => (
          <Box component="li" key={entry.id} sx={{ listStyle: 'none' }}>
            {index > 0 && <Divider component="div" />}
            <ListItem component="div" sx={{ display: 'block', px: 0, py: 1.5 }}>
              <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, alignItems: 'baseline' }}>
                <Typography sx={{ fontWeight: 600 }}>{actionLabel(entry.action)}</Typography>
                <Typography variant="body2" color="textSecondary">
                  {entry.userName} · {formatDateTime(entry.timestamp)}
                </Typography>
              </Box>
              <Box component="ul" sx={{ m: 0, mt: 0.5, pl: 2.5 }}>
                {changesOf(entry).map((change) => (
                  <Typography component="li" variant="body2" key={change.field} sx={{ overflowWrap: 'anywhere' }}>
                    {change.field}: {change.text}
                  </Typography>
                ))}
              </Box>
            </ListItem>
          </Box>
        ))}
      </List>
      {totalPages > 1 && (
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1, flexWrap: 'wrap', pt: 1 }}>
          <Typography variant="body2" color="textSecondary">
            {totalCount} kayıt · Sayfa {page} / {totalPages}
          </Typography>
          <Box sx={{ display: 'flex', gap: 1 }}>
            <Button size="small" disabled={page <= 1} onClick={() => setPage(page - 1)}>
              Daha yeni
            </Button>
            <Button size="small" disabled={page >= totalPages} onClick={() => setPage(page + 1)}>
              Daha eski
            </Button>
          </Box>
        </Box>
      )}
    </Box>
  )
}
