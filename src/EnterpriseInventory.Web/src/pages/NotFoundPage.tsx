import SearchOffIcon from '@mui/icons-material/SearchOff'
import { Button, Card, CardContent } from '@mui/material'
import { Link as RouterLink } from 'react-router'
import { PageHeader } from '../components/PageHeader'
import { EmptyState } from '../components/states/EmptyState'

export function NotFoundPage() {
  return (
    <>
      <PageHeader title="Sayfa bulunamadı" />
      <Card>
        <CardContent>
          <EmptyState
            icon={<SearchOffIcon />}
            title="Aradığınız sayfa bulunamadı"
            description="Adres yanlış yazılmış ya da sayfa kaldırılmış olabilir."
            action={
              <Button variant="contained" component={RouterLink} to="/">
                Gösterge Paneline dön
              </Button>
            }
          />
        </CardContent>
      </Card>
    </>
  )
}
