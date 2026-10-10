import type { AuditEntityName, AuditLogEntry } from './auditApi'

export const entityLabels: Record<AuditEntityName, string> = {
  Asset: 'Demirbaş',
  Brand: 'Marka',
  AssetModel: 'Model',
  City: 'Şehir',
  Department: 'Departman',
  Location: 'Lokasyon',
  AdminUser: 'Yönetici',
}

/** A kind of record in Turkish; a kind this version does not know is shown as sent. */
export function entityLabel(entityName: string) {
  return entityLabels[entityName as AuditEntityName] ?? entityName
}

/** How a record is named on the screen: its current name, or its number when it no longer exists. */
export function recordName(entry: Pick<AuditLogEntry, 'entityLabel' | 'entityId'>) {
  return entry.entityLabel ?? `#${entry.entityId} (artık yok)`
}

/** The asset's detail page for an asset record that still exists. */
export function assetLink(entry: Pick<AuditLogEntry, 'entityName' | 'entityLabel' | 'entityId'>) {
  return entry.entityName === 'Asset' && entry.entityLabel !== null ? `/envanter/${entry.entityId}` : null
}
