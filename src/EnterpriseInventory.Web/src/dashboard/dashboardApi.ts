import { apiFetch } from '../api/http'

export interface DistributionItem {
  id: number
  name: string
  count: number
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
  byCity: DistributionItem[]
  byDepartment: DistributionItem[]
  recentActivity: RecentActivity[]
}

export const dashboardQueryKey = ['dashboard', 'statistics'] as const

export function fetchDashboardStatistics(signal?: AbortSignal) {
  return apiFetch<DashboardStatistics>('/api/dashboard/statistics', { signal })
}
