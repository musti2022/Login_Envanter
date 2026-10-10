import SearchOffIcon from '@mui/icons-material/SearchOff'
import { Button, Card } from '@mui/material'
import { Link as RouterLink } from 'react-router'
import { EmptyState } from '../components/states/EmptyState'

export function AssetNotFound() {
  return (
    <Card>
      <EmptyState
        icon={<SearchOffIcon />}
        title="Demirbaş bulunamadı"
        description="Kayıt silinmiş veya adres yanlış olabilir."
        action={
          <Button variant="contained" component={RouterLink} to="/envanter">
            Envantere dön
          </Button>
        }
      />
    </Card>
  )
}
