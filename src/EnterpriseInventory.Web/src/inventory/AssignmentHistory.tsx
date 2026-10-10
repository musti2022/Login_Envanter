import { Box, Button, Chip, Divider, LinearProgress, List, ListItem, Typography } from '@mui/material'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { EmptyState } from '../components/states/EmptyState'
import { ErrorState } from '../components/states/ErrorState'
import { LoadingState } from '../components/states/LoadingState'
import { assetAssignmentsQueryKey, fetchAssetAssignments } from './assignmentsApi'
import { formatDateTime } from './labels'

/** Who held the asset and when, newest first, a page at a time. Returned periods stay listed. */
export function AssignmentHistory({ assetId }: { assetId: number }) {
  const [page, setPage] = useState(1)
  const assignments = useQuery({
    queryKey: assetAssignmentsQueryKey(assetId, page),
    queryFn: ({ signal }) => fetchAssetAssignments(assetId, page, signal),
    placeholderData: keepPreviousData,
  })

  if (assignments.isPending) return <LoadingState message="Zimmet geçmişi yükleniyor..." />
  if (assignments.isError) {
    return (
      <ErrorState
        title="Zimmet geçmişi yüklenemedi"
        message="Bağlantınızı kontrol edip tekrar deneyin."
        onRetry={() => void assignments.refetch()}
      />
    )
  }
  if (assignments.data.totalCount === 0) {
    return <EmptyState title="Henüz zimmet kaydı yok" description="Demirbaş bir çalışana zimmetlendiğinde burada listelenir." />
  }

  const { items, totalPages, totalCount } = assignments.data
  return (
    <Box>
      <Box sx={{ height: 4 }}>{assignments.isFetching && <LinearProgress aria-label="Zimmet geçmişi yenileniyor" />}</Box>
      <List aria-label="Zimmet geçmişi" disablePadding>
        {items.map((item, index) => (
          <Box component="li" key={item.id} sx={{ listStyle: 'none' }}>
            {index > 0 && <Divider component="div" />}
            <ListItem component="div" sx={{ display: 'block', px: 0, py: 1.5 }}>
              <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, alignItems: 'center' }}>
                <Typography sx={{ fontWeight: 600 }}>
                  {item.displayName} ({item.userName})
                </Typography>
                {item.returnedAt === null ? (
                  <Chip size="small" color="primary" label="Zimmette" />
                ) : (
                  <Chip size="small" variant="outlined" label="İade alındı" />
                )}
              </Box>
              {item.department && (
                <Typography variant="body2" color="textSecondary">
                  {item.department}
                </Typography>
              )}
              <Box component="dl" sx={{ m: 0, mt: 0.5, display: 'grid', gridTemplateColumns: 'auto minmax(0, 1fr)', columnGap: 1.5, rowGap: 0.25 }}>
                <Term>Zimmet</Term>
                <Detail>
                  {formatDateTime(item.assignedAt)} · {item.assignedBy}
                </Detail>
                {item.returnedAt && (
                  <>
                    <Term>İade</Term>
                    <Detail>
                      {formatDateTime(item.returnedAt)} · {item.returnedBy}
                    </Detail>
                  </>
                )}
                {item.assignmentDescription && (
                  <>
                    <Term>Tanım</Term>
                    <Detail>{item.assignmentDescription}</Detail>
                  </>
                )}
                {item.notes && (
                  <>
                    <Term>Not</Term>
                    <Detail>{item.notes}</Detail>
                  </>
                )}
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

function Term({ children }: { children: string }) {
  return (
    <Typography component="dt" variant="body2" color="textSecondary">
      {children}
    </Typography>
  )
}

function Detail({ children }: { children: ReactNode }) {
  return (
    <Typography component="dd" variant="body2" sx={{ m: 0, overflowWrap: 'anywhere', whiteSpace: 'pre-line' }}>
      {children}
    </Typography>
  )
}
