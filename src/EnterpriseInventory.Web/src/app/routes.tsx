import type { RouteObject } from 'react-router'
import { RequireAuth, signInPath } from '../auth/RequireAuth'
import { AppLayout } from '../layout/AppLayout'
import { AssetCreatePage } from '../pages/AssetCreatePage'
import { AssetDetailPage } from '../pages/AssetDetailPage'
import { AssetEditPage } from '../pages/AssetEditPage'
import { AuditLogPage } from '../pages/AuditLogPage'
import { BrandsModelsPage } from '../pages/BrandsModelsPage'
import { DashboardPage } from '../pages/DashboardPage'
import { InventoryPage } from '../pages/InventoryPage'
import { LocationsPage } from '../pages/LocationsPage'
import { LoginPage } from '../pages/LoginPage'
import { NotFoundPage } from '../pages/NotFoundPage'
import { ReportsPage } from '../pages/ReportsPage'
import { RouteErrorPage } from '../pages/RouteErrorPage'

export const routes: RouteObject[] = [
  { path: signInPath, element: <LoginPage />, errorElement: <RouteErrorPage /> },
  {
    path: '/',
    element: <RequireAuth />,
    errorElement: <RouteErrorPage />,
    children: [
      {
        element: <AppLayout />,
        children: [
          { index: true, element: <DashboardPage /> },
          { path: 'envanter', element: <InventoryPage /> },
          { path: 'envanter/yeni', element: <AssetCreatePage /> },
          { path: 'envanter/:id', element: <AssetDetailPage /> },
          { path: 'envanter/:id/duzenle', element: <AssetEditPage /> },
          { path: 'zimmetler', element: <InventoryPage assignedOnly /> },
          { path: 'tanimlar/lokasyonlar', element: <LocationsPage /> },
          { path: 'tanimlar/marka-model', element: <BrandsModelsPage /> },
          { path: 'raporlar', element: <ReportsPage /> },
          { path: 'raporlar/zimmet-hareketleri', element: <ReportsPage /> },
          { path: 'denetim-gecmisi', element: <AuditLogPage /> },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
]
