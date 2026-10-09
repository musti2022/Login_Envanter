import type { AssetHistoryEntry } from './assetsApi'
import { formatDateTime, statusLabels, typeLabels, type AssetStatus, type AssetType } from './labels'

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
  employeeDisplayName: 'Çalışan',
  employeeUserName: 'Kullanıcı adı',
  assignmentDescription: 'Zimmet tanımı',
  notes: 'Not',
  assignedAt: 'Zimmet tarihi',
  returnedAt: 'İade tarihi',
}

/** Recorded for tracing, not for reading: the directory GUID of the employee. */
const hiddenFields = new Set(['employeeObjectGuid'])

function formatValue(field: string, value: unknown): string {
  if (value === null || value === undefined || value === '') return '—'
  if (field === 'status') return statusLabels[value as AssetStatus] ?? String(value)
  if (field === 'assetType') return typeLabels[value as AssetType] ?? String(value)
  if (typeof value === 'boolean') return value ? 'Evet' : 'Hayır'
  if (field.endsWith('At') && typeof value === 'string') return formatDateTime(value)
  return String(value)
}

/**
 * The fields an entry changed, each with the text the history shows: "before → after" for a changed value, the
 * value alone when the entry records it on one side only (what an asset was created with, who it was assigned
 * to, who returned it). A field this version does not know is shown under its own name.
 */
export function changesOf(entry: AssetHistoryEntry) {
  const oldValues = entry.oldValues ?? {}
  const newValues = entry.newValues ?? {}
  const keys = [...new Set([...Object.keys(oldValues), ...Object.keys(newValues)])].filter(
    (key) => !key.endsWith('Id') && !hiddenFields.has(key),
  )
  const order = Object.keys(fieldLabels)
  keys.sort((a, b) => (order.indexOf(a) + 1 || order.length + 1) - (order.indexOf(b) + 1 || order.length + 1))
  return keys
    .map((key) => {
      const before = formatValue(key, oldValues[key])
      const after = formatValue(key, newValues[key])
      const text = !(key in oldValues) ? after : !(key in newValues) ? before : `${before} → ${after}`
      return { field: fieldLabels[key] ?? key, before, after, text }
    })
    .filter((change) => change.before !== change.after)
}
