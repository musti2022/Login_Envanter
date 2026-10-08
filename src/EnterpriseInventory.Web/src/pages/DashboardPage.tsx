import { Card, CardContent, Grid, Typography } from '@mui/material'
import { PageHeader } from '../components/PageHeader'
import { EmptyState } from '../components/states/EmptyState'

const kpiCards = ['Toplam Demirbaş', 'Zimmetli', 'Boşta', 'Arızalı'] as const

export function DashboardPage() {
  return (
    <>
      <PageHeader title="Gösterge Paneli" description="Demirbaş durumunun genel görünümü." />

      <Grid container spacing={2} sx={{ mb: 3 }}>
        {kpiCards.map((label) => (
          <Grid key={label} size={{ xs: 12, sm: 6, lg: 3 }}>
            <Card>
              <CardContent>
                <Typography color="text.secondary" gutterBottom>
                  {label}
                </Typography>
                <Typography variant="h4" component="p" color="primary" aria-label={`${label}: veri yok`}>
                  —
                </Typography>
              </CardContent>
            </Card>
          </Grid>
        ))}
      </Grid>

      <Card>
        <CardContent>
          <EmptyState
            title="Henüz istatistik yok"
            description="Şehir ve departman dağılımı ile son işlemler, envanter verileri bağlandığında burada görüntülenecek."
          />
        </CardContent>
      </Card>
    </>
  )
}
