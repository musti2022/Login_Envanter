import { apiFetch } from '../api/http'
import type { AssetType } from '../inventory/labels'

/** A city, department or brand: its assets and how many of them are assigned. */
export interface DistributionItem {
  id: number
  name: string
  count: number
  assignedCount: number
}

/**
 * The eight cities, departments or brands with the most assets, largest first, and every other one counted together.
 */
export interface Distribution {
  items: DistributionItem[]
  /** How many cities, departments or brands are counted together. */
  otherGroupCount: number
  otherCount: number
  otherAssignedCount: number
}

export interface TypeCount {
  assetType: AssetType
  count: number
}

/** Assignments made and returns taken in a month of the reporting time zone. */
export interface MonthlyMovement {
  /** yyyy-MM */
  month: string
  assignedCount: number
  returnedCount: number
}

export interface RecentActivity {
  id: number
  assetId: number
  /** null when the asset no longer exists. */
  assetCode: string | null
  action: string
  userName: string
  timestamp: string
}

/** Figures of GET /api/dashboard/statistics; counts leave archived assets out, except archivedCount. */
export interface DashboardStatistics {
  totalCount: number
  assignedCount: number
  availableCount: number
  faultyCount: number
  retiredCount: number
  archivedCount: number
  byCity: Distribution
  byDepartment: Distribution
  byType: TypeCount[]
  byBrand: Distribution
  /** The last 12 months, oldest first; archived assets' movements included. */
  monthlyMovements: MonthlyMovement[]
  recentActivity: RecentActivity[]
}

export const dashboardQueryKey = ['dashboard', 'statistics'] as const

export function fetchDashboardStatistics(signal?: AbortSignal) {
  return apiFetch<DashboardStatistics>('/api/dashboard/statistics', { signal })
}
