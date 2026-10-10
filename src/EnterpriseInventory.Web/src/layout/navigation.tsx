import AssessmentIcon from '@mui/icons-material/Assessment'
import AssignmentIndIcon from '@mui/icons-material/AssignmentInd'
import CategoryIcon from '@mui/icons-material/Category'
import DashboardIcon from '@mui/icons-material/Dashboard'
import HistoryIcon from '@mui/icons-material/History'
import Inventory2Icon from '@mui/icons-material/Inventory2'
import PlaceIcon from '@mui/icons-material/Place'
import type { ReactElement } from 'react'

export interface NavigationItem {
  label: string
  path: string
  icon: ReactElement
}

export const navigationItems: readonly NavigationItem[] = [
  { label: 'Gösterge Paneli', path: '/', icon: <DashboardIcon /> },
  { label: 'Envanter', path: '/envanter', icon: <Inventory2Icon /> },
  { label: 'Zimmetler', path: '/zimmetler', icon: <AssignmentIndIcon /> },
  { label: 'Lokasyonlar', path: '/tanimlar/lokasyonlar', icon: <PlaceIcon /> },
  { label: 'Marka ve Modeller', path: '/tanimlar/marka-model', icon: <CategoryIcon /> },
  { label: 'Raporlar', path: '/raporlar', icon: <AssessmentIcon /> },
  { label: 'Denetim Geçmişi', path: '/denetim-gecmisi', icon: <HistoryIcon /> },
]
