import { Box, Tab, Tabs } from '@mui/material'
import { Link as RouterLink, useLocation } from 'react-router'
import { PageHeader } from '../components/PageHeader'
import { AssetSummaryReport } from '../reports/AssetSummaryReport'
import { AssignmentMovementsReport } from '../reports/AssignmentMovementsReport'

const movementsPath = '/raporlar/zimmet-hareketleri'

/** "Raporlar": the inventory summary and the assignment movements, each with Turkish filters and Excel export. */
export function ReportsPage() {
  const { pathname } = useLocation()
  const tab = pathname.replace(/\/$/, '') === movementsPath ? 'movements' : 'summary'

  return (
    <>
      <PageHeader title="Raporlar" description="Envanter ve zimmet raporlarını görüntüleyin ve Excel'e aktarın." />
      <Box sx={{ borderBottom: 1, borderColor: 'divider', mb: 2 }}>
        <Tabs value={tab} aria-label="Rapor türü" variant="scrollable" allowScrollButtonsMobile>
          <Tab value="summary" label="Envanter özeti" component={RouterLink} to="/raporlar" />
          <Tab value="movements" label="Zimmet hareketleri" component={RouterLink} to={movementsPath} />
        </Tabs>
      </Box>
      {tab === 'summary' ? <AssetSummaryReport /> : <AssignmentMovementsReport />}
    </>
  )
}
