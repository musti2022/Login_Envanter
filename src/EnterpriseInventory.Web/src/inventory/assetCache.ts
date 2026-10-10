import type { QueryClient } from '@tanstack/react-query'
import { auditLogsQueryKey } from '../audit/auditApi'
import { dashboardQueryKey } from '../dashboard/dashboardApi'
import { assetQueryKey, assetsQueryKey, type AssetDetails } from './assetsApi'

/**
 * After a save: the saved asset is the detail page's data, and everything that counts or lists assets (lists,
 * histories, the dashboard, the audit log) is fetched again when next shown.
 */
export function assetSaved(queryClient: QueryClient, asset: AssetDetails) {
  queryClient.setQueryData(assetQueryKey(asset.id), asset)
  void queryClient.invalidateQueries({
    queryKey: assetsQueryKey,
    predicate: (query) => query.queryKey[1] !== 'detail' || query.queryKey[2] !== asset.id,
  })
  void queryClient.invalidateQueries({ queryKey: dashboardQueryKey })
  void queryClient.invalidateQueries({ queryKey: auditLogsQueryKey })
}
