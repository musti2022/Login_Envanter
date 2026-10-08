import type { RouteObject } from 'react-router'
import { AppLayout } from '../layout/AppLayout'
import { DashboardPage } from '../pages/DashboardPage'
import { NotFoundPage } from '../pages/NotFoundPage'
import { PlaceholderPage } from '../pages/PlaceholderPage'
import { RouteErrorPage } from '../pages/RouteErrorPage'

export const routes: RouteObject[] = [
  {
    path: '/',
    element: <AppLayout />,
    errorElement: <RouteErrorPage />,
    children: [
      { index: true, element: <DashboardPage /> },
      {
        path: 'envanter',
        element: (
          <PlaceholderPage
            title="Envanter"
            description="Demirbaşları listeleyin, arayın ve yönetin."
            emptyTitle="Henüz demirbaş kaydı yok"
            emptyDescription="Envanter tablosu; arama, filtreleme ve Excel dışa aktarma ile birlikte sonraki aşamada eklenecek."
          />
        ),
      },
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
      {
        path: 'denetim-gecmisi',
        element: (
          <PlaceholderPage
            title="Denetim Geçmişi"
            description="Kayıtlar üzerinde yapılan tüm değişiklikleri izleyin."
            emptyTitle="Henüz denetim kaydı yok"
            emptyDescription="Denetim kayıtları, veri değişiklikleri başladığında burada listelenecek."
          />
        ),
      },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
]
