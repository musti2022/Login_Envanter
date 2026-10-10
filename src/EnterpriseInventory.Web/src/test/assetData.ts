import type { AssetDetails, AssetListItem, PagedResult } from '../inventory/assetsApi'

/** A list row with every column filled; tests override what they look at. */
export function listItem(overrides: Partial<AssetListItem> = {}): AssetListItem {
  return {
    id: 1,
    assetCode: 'DMR-0001',
    computerName: 'PC-IST-01',
    brandName: 'Dell',
    modelName: 'Latitude 5440',
    serialNumber: 'SN-100',
    assetType: 'Laptop',
    status: 'Assigned',
    cityName: 'İstanbul',
    departmentName: 'Bilgi İşlem',
    locationName: 'Merkez Ofis',
    assignedUserName: 'ali.kaya',
    assignedDisplayName: 'Ali Kaya',
    assignmentDescription: 'Dizüstü + çanta',
    isArchived: false,
    createdAt: '2026-10-01T08:00:00Z',
    updatedAt: '2026-10-05T09:30:00Z',
    ...overrides,
  }
}

export function pageOf(items: AssetListItem[], options: Partial<PagedResult<AssetListItem>> = {}): PagedResult<AssetListItem> {
  const pageSize = options.pageSize ?? 25
  const totalCount = options.totalCount ?? items.length
  return {
    items,
    page: 1,
    pageSize,
    totalCount,
    totalPages: totalCount === 0 ? 0 : Math.ceil(totalCount / pageSize),
    ...options,
  }
}

/** GET /api/assets/{id} of an available laptop on the lookups of lookupData.ts. */
export function details(overrides: Partial<AssetDetails> = {}): AssetDetails {
  return {
    id: 1,
    assetCode: 'DMR-0001',
    computerName: 'PC-IST-01',
    assetType: 'Laptop',
    status: 'Available',
    serialNumber: 'SN-100',
    description: null,
    brand: { id: 1, name: 'Dell' },
    model: { id: 11, name: 'Latitude 5440' },
    city: { id: 6, name: 'İstanbul' },
    department: { id: 8, name: 'Bilgi İşlem' },
    location: { id: 60, name: 'Merkez Ofis' },
    activeAssignment: null,
    isArchived: false,
    createdAt: '2026-10-01T08:00:00Z',
    createdBy: 'ayse.yilmaz',
    updatedAt: null,
    updatedBy: null,
    rowVersion: 'AAAAAAAAB9E=',
    ...overrides,
  }
}
