import type { RouteObject } from 'react-router'
import { RequireAuth, signInPath } from '../auth/RequireAuth'
import { AppLayout } from '../layout/AppLayout'
import { AssetCreatePage } from '../pages/AssetCreatePage'
import { AssetDetailPage } from '../pages/AssetDetailPage'
import { AssetEditPage } from '../pages/AssetEditPage'
import { AuditLogPage } from '../pages/AuditLogPage'
import { DashboardPage } from '../pages/DashboardPage'
import { InventoryPage } from '../pages/InventoryPage'
import { LoginPage } from '../pages/LoginPage'
import { NotFoundPage } from '../pages/NotFoundPage'
import { PlaceholderPage } from '../pages/PlaceholderPage'
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
          {
            path: 'zimmetler',
            element: (
              <PlaceholderPage
                title="Zimmetler"
                description="Demirbaş zimmetlerini oluşturun, iade alın ve geçmişi görüntüleyin."
                emptyTitle="Henüz zimmet kaydı yok"
                emptyDescription="Zimmet işlemleri ve Active Directory çalışan araması sonraki aşamada eklenecek."
              />
            ),
          },
          {
            path: 'tanimlar/lokasyonlar',
            element: (
              <PlaceholderPage
                title="Lokasyonlar"
                description="Şehir, departman ve lokasyon tanımlarını yönetin."
                emptyTitle="Henüz lokasyon tanımı yok"
                emptyDescription="Şehir, departman ve lokasyon yönetimi sonraki aşamada eklenecek."
              />
            ),
          },
          {
            path: 'tanimlar/marka-model',
            element: (
              <PlaceholderPage
                title="Marka ve Modeller"
                description="Demirbaş marka ve model tanımlarını yönetin."
                emptyTitle="Henüz marka veya model tanımı yok"
                emptyDescription="Marka ve model yönetimi sonraki aşamada eklenecek."
              />
            ),
          },
          {
            path: 'raporlar',
            element: (
              <PlaceholderPage
                title="Raporlar"
                description="Envanter ve zimmet raporlarını görüntüleyin ve dışa aktarın."
                emptyTitle="Henüz rapor yok"
                emptyDescription="Raporlar, envanter ve zimmet verileri oluştuktan sonra eklenecek."
              />
            ),
          },
          { path: 'denetim-gecmisi', element: <AuditLogPage /> },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
]
