import type { AssetHistoryEntry } from './assetsApi'
import { statusLabels, typeLabels, type AssetStatus, type AssetType } from './labels'

/** Recorded fields in the order they are shown; IDs are left out because the names stand next to them. */
const fieldLabels: Record<string, string> = {
  assetCode: 'Demirbaş kodu',
  assetType: 'Tür',
  status: 'Durum',
  computerName: 'Bilgisayar adı',
  serialNumber: 'Seri no',
  brandName: 'Marka',
  modelName: 'Model',
  cityName: 'Şehir',
  locationName: 'Lokasyon',
  departmentName: 'Departman',
  description: 'Açıklama',
  isArchived: 'Arşivlendi',
}

function formatValue(field: string, value: unknown): string {
  if (value === null || value === undefined || value === '') return '—'
  if (field === 'status') return statusLabels[value as AssetStatus] ?? String(value)
  if (field === 'assetType') return typeLabels[value as AssetType] ?? String(value)
  if (typeof value === 'boolean') return value ? 'Evet' : 'Hayır'
  return String(value)
}

/** The fields an entry changed; a field this version does not know is shown under its own name. */
export function changesOf(entry: AssetHistoryEntry) {
  const oldValues = entry.oldValues ?? {}
  const newValues = entry.newValues ?? {}
  const keys = [...new Set([...Object.keys(oldValues), ...Object.keys(newValues)])].filter((key) => !key.endsWith('Id'))
  const order = Object.keys(fieldLabels)
  keys.sort((a, b) => (order.indexOf(a) + 1 || order.length + 1) - (order.indexOf(b) + 1 || order.length + 1))
  return keys
    .map((key) => ({
      field: fieldLabels[key] ?? key,
      before: formatValue(key, oldValues[key]),
      after: formatValue(key, newValues[key]),
    }))
    .filter((change) => change.before !== change.after)
}
