import { Box, Card, CardActionArea, CardContent, Grid, Link, List, ListItem, ListItemText, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { Link as RouterLink } from 'react-router'
import { PageHeader } from '../components/PageHeader'
import { EmptyState } from '../components/states/EmptyState'
import { ErrorState } from '../components/states/ErrorState'
import { LoadingState } from '../components/states/LoadingState'
import { DistributionList, type DistributionRow } from '../dashboard/DistributionList'
import { MonthlyMovementsChart } from '../dashboard/MonthlyMovementsChart'
import {
  dashboardQueryKey,
  fetchDashboardStatistics,
  type DashboardStatistics,
  type Distribution,
} from '../dashboard/dashboardApi'
import { actionLabel, formatDateTime, formatNumber, typeLabels } from '../inventory/labels'

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
            {statistics.byCity.items.length > 0 ? (
              <DistributionList
                label="Şehirlere göre demirbaş sayısı"
                rows={distributionRows(statistics.byCity, { other: 'şehir', filter: 'cityId', split: true })}
              />
            ) : (
              <NoAssets />
            )}
          </Section>
        </Grid>
        <Grid size={{ xs: 12, md: 6, lg: 4 }}>
          <Section title="Departmanlara göre dağılım">
            {statistics.byDepartment.items.length > 0 ? (
              <DistributionList
                label="Departmanlara göre demirbaş sayısı"
                rows={distributionRows(statistics.byDepartment, { other: 'departman', filter: 'departmentId', split: true })}
              />
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

      <Grid container spacing={2} sx={{ mt: 0 }}>
        <Grid size={{ xs: 12, lg: 6 }}>
          <Section title="Aylık zimmet hareketleri">
            <MonthlyMovementsChart months={statistics.monthlyMovements} />
          </Section>
        </Grid>
        <Grid size={{ xs: 12, md: 6, lg: 3 }}>
          <Section title="Türlere göre dağılım">
            {statistics.byType.length > 0 ? (
              <DistributionList
                label="Türlere göre demirbaş sayısı"
                rows={statistics.byType.map((t) => ({
                  key: t.assetType,
                  name: typeLabels[t.assetType] ?? t.assetType,
                  count: t.count,
                  to: `/envanter?assetType=${t.assetType}`,
                }))}
              />
            ) : (
              <NoAssets />
            )}
          </Section>
        </Grid>
        <Grid size={{ xs: 12, md: 6, lg: 3 }}>
          <Section title="Markalara göre dağılım">
            {statistics.byBrand.items.length > 0 ? (
              <DistributionList
                label="Markalara göre demirbaş sayısı"
                rows={distributionRows(statistics.byBrand, { other: 'marka', filter: 'brandId', split: false })}
              />
            ) : (
              <NoAssets />
            )}
          </Section>
        </Grid>
      </Grid>
    </>
  )
}

interface RowOptions {
  /** What the rest are, e.g. "şehir" for "Diğer 12 şehir". */
  other: string
  /** The inventory filter of a row. */
  filter: 'cityId' | 'departmentId' | 'brandId'
  /** Whether the bars show the assigned part. */
  split: boolean
}

/** One row per named city, department or brand, linked to the filtered inventory, and one for the rest. */
function distributionRows(distribution: Distribution, { other, filter, split }: RowOptions): DistributionRow[] {
  const rows: DistributionRow[] = distribution.items.map((item) => ({
    key: item.id,
    name: item.name,
    count: item.count,
    ...(split && { assignedCount: item.assignedCount }),
    to: `/envanter?${filter}=${item.id}`,
  }))
  if (distribution.otherGroupCount > 0) {
    rows.push({
      key: 'other',
      name: `Diğer ${formatNumber(distribution.otherGroupCount)} ${other}`,
      count: distribution.otherCount,
      ...(split && { assignedCount: distribution.otherAssignedCount }),
    })
  }
  return rows
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
