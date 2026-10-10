import type { QueryClient } from '@tanstack/react-query'
import { inBackground } from '../api/http'
import { auditLogsQueryKey } from '../audit/auditApi'
import { dashboardQueryKey } from '../dashboard/dashboardApi'
import { assetsQueryKey } from '../inventory/assetsApi'

/**
 * A live notification says an asset changed: everything that shows it is fetched again from the API, the source of
 * truth. Shown data is refetched now, everything else when next shown. Lists, the dashboard and the audit log can
 * change with any asset; details, history and assignments only with their own. These reads are background reads.
 */
export function refreshAfterChange(queryClient: QueryClient, assetId: number) {
  inBackground(() => {
    void queryClient.invalidateQueries({
      queryKey: assetsQueryKey,
      predicate: (query) => query.queryKey[1] === 'list' || query.queryKey[2] === assetId,
    })
    void queryClient.invalidateQueries({ queryKey: dashboardQueryKey })
    void queryClient.invalidateQueries({ queryKey: auditLogsQueryKey })
  })
}

/**
 * The live connection was down, so notifications may have been missed: everything cached is fetched again, shown
 * data now and the rest when next shown, as background reads. The session's own queries are left alone.
 */
export function refreshEverything(queryClient: QueryClient) {
  inBackground(() => {
    void queryClient.invalidateQueries({ predicate: (query) => query.queryKey[0] !== 'auth' })
  })
}
