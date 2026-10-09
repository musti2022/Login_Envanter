/** Turkish labels for the values the API exchanges by name. */

export const assetStatuses = ['Available', 'Assigned', 'Faulty', 'Retired'] as const
export type AssetStatus = (typeof assetStatuses)[number]

export const statusLabels: Record<AssetStatus, string> = {
  Available: 'Boşta',
  Assigned: 'Zimmetli',
  Faulty: 'Arızalı',
  Retired: 'Hurda',
}

export const assetTypes = [
  'Desktop',
  'Laptop',
  'Monitor',
  'Printer',
  'Phone',
  'Tablet',
  'Server',
  'NetworkDevice',
  'Peripheral',
  'Other',
] as const
export type AssetType = (typeof assetTypes)[number]

export const typeLabels: Record<AssetType, string> = {
  Desktop: 'Masaüstü',
  Laptop: 'Dizüstü',
  Monitor: 'Monitör',
  Printer: 'Yazıcı',
  Phone: 'Telefon',
  Tablet: 'Tablet',
  Server: 'Sunucu',
  NetworkDevice: 'Ağ cihazı',
  Peripheral: 'Çevre birimi',
  Other: 'Diğer',
}

const actionLabels: Record<string, string> = {
  Created: 'Eklendi',
  Updated: 'Güncellendi',
  Archived: 'Arşivlendi',
  Assigned: 'Zimmetlendi',
  Returned: 'İade alındı',
  LocationChanged: 'Konumu değişti',
  StatusChanged: 'Durumu değişti',
}

/** An audit action in Turkish; an action this version does not know is shown as sent. */
export function actionLabel(action: string) {
  return actionLabels[action] ?? action
}

const numberFormat = new Intl.NumberFormat('tr-TR')
const dateTimeFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'short', timeStyle: 'short' })
const dateFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'short' })

export const formatNumber = (value: number) => numberFormat.format(value)
export const formatDateTime = (value: string) => dateTimeFormat.format(new Date(value))
export const formatDate = (value: string) => dateFormat.format(new Date(value))
