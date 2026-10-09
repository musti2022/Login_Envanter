import { Box, Card, CardActionArea, CardContent, Grid, Link, List, ListItem, ListItemText, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { Link as RouterLink } from 'react-router'
import { PageHeader } from '../components/PageHeader'
import { EmptyState } from '../components/states/EmptyState'
import { ErrorState } from '../components/states/ErrorState'
import { LoadingState } from '../components/states/LoadingState'
import { DistributionList } from '../dashboard/DistributionList'
import { dashboardQueryKey, fetchDashboardStatistics, type DashboardStatistics } from '../dashboard/dashboardApi'
import { actionLabel, formatDateTime, formatNumber } from '../inventory/labels'

interface Kpi {
  label: string
  value: (statistics: DashboardStatistics) => number
  /** The inventory, filtered to what the card counts. */
  to: string
}

const kpis: readonly Kpi[] = [
  { label: 'Toplam Demirbaş', value: (s) => s.totalCount, to: '/envanter' },
  { label: 'Zimmetli', value: (s) => s.assignedCount, to: '/envanter?status=Assigned' },
  { label: 'Boşta', value: (s) => s.availableCount, to: '/envanter?status=Available' },
  { label: 'Arızalı', value: (s) => s.faultyCount, to: '/envanter?status=Faulty' },
]

export function DashboardPage() {
  const statistics = useQuery({ queryKey: dashboardQueryKey, queryFn: ({ signal }) => fetchDashboardStatistics(signal) })

  return (
    <>
      <PageHeader title="Gösterge Paneli" description="Demirbaş durumunun genel görünümü." />
      {statistics.isPending ? (
        <LoadingState message="İstatistikler yükleniyor..." />
      ) : statistics.isError ? (
        <ErrorState
          title="İstatistikler yüklenemedi"
          message="Gösterge paneli verileri alınamadı. Bağlantınızı kontrol edip tekrar deneyin."
          onRetry={() => void statistics.refetch()}
        />
      ) : (
        <DashboardContent statistics={statistics.data} />
      )}
    </>
  )
}

function DashboardContent({ statistics }: { statistics: DashboardStatistics }) {
  return (
    <>
      <Grid container spacing={2} sx={{ mb: 1 }}>
        {kpis.map((kpi) => (
          <Grid key={kpi.label} size={{ xs: 12, sm: 6, lg: 3 }}>
            <Card sx={{ height: '100%' }}>
              <CardActionArea component={RouterLink} to={kpi.to} sx={{ height: '100%' }}>
                <CardContent>
                  <Typography color="textSecondary" gutterBottom>
                    {kpi.label}
                  </Typography>
                  <Typography variant="h4" component="p" color="primary" sx={{ fontWeight: 700 }}>
                    {formatNumber(kpi.value(statistics))}
                  </Typography>
                </CardContent>
              </CardActionArea>
            </Card>
          </Grid>
        ))}
      </Grid>
      <Typography variant="body2" color="textSecondary" sx={{ mb: 3 }}>
        Hurda: {formatNumber(statistics.retiredCount)} · Arşivlenmiş: {formatNumber(statistics.archivedCount)}. Toplam,
        arşivlenmiş demirbaşları içermez.
      </Typography>

      <Grid container spacing={2}>
        <Grid size={{ xs: 12, md: 6, lg: 4 }}>
          <Section title="Şehirlere göre dağılım">
            {statistics.byCity.length > 0 ? (
              <DistributionList label="Şehirlere göre demirbaş sayısı" items={statistics.byCity} />
            ) : (
              <NoAssets />
            )}
          </Section>
        </Grid>
        <Grid size={{ xs: 12, md: 6, lg: 4 }}>
          <Section title="Departmanlara göre dağılım">
            {statistics.byDepartment.length > 0 ? (
              <DistributionList label="Departmanlara göre demirbaş sayısı" items={statistics.byDepartment} />
            ) : (
              <NoAssets />
            )}
          </Section>
        </Grid>
        <Grid size={{ xs: 12, lg: 4 }}>
          <Section title="Son işlemler">
            {statistics.recentActivity.length > 0 ? (
              <List dense disablePadding aria-label="Son işlemler">
                {statistics.recentActivity.map((activity) => (
                  <ListItem key={activity.id} disableGutters divider>
                    <ListItemText
                      primary={
                        <>
                          {activity.assetCode ? (
                            <Link component={RouterLink} to={`/envanter/${activity.assetId}`} sx={{ fontWeight: 600 }}>
                              {activity.assetCode}
                            </Link>
                          ) : (
                            'Silinmiş kayıt'
                          )}{' '}
                          {actionLabel(activity.action).toLocaleLowerCase('tr-TR')}
                        </>
                      }
                      secondary={`${activity.userName} · ${formatDateTime(activity.timestamp)}`}
                    />
                  </ListItem>
                ))}
              </List>
            ) : (
              <EmptyState title="Henüz işlem yok" description="Demirbaş eklendiğinde veya değiştirildiğinde burada görünür." />
            )}
          </Section>
        </Grid>
      </Grid>
    </>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Typography variant="h6" component="h2" sx={{ mb: 2 }}>
          {title}
        </Typography>
        <Box>{children}</Box>
      </CardContent>
    </Card>
  )
}

function NoAssets() {
  return (
    <EmptyState
      title="Henüz demirbaş yok"
      description="Envantere demirbaş eklendiğinde dağılım burada görünür."
      action={
        <Link component={RouterLink} to="/envanter">
          Envantere git
        </Link>
      }
    />
  )
}
