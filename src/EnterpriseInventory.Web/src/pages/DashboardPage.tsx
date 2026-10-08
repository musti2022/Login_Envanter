import { Box, Card, CardContent, Grid, Typography } from '@mui/material'
import { visuallyHidden } from '@mui/utils'
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
                <Typography color="textSecondary" gutterBottom>
                  {label}
                </Typography>
                <Typography variant="h4" component="p" color="primary">
                  <span aria-hidden="true">—</span>
                  <Box component="span" sx={visuallyHidden}>
                    Veri yok
                  </Box>
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
