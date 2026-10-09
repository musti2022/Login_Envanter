import type { QueryClient } from '@tanstack/react-query'
import { inBackground } from '../api/http'
import { dashboardQueryKey } from '../dashboard/dashboardApi'
import { assetsQueryKey } from '../inventory/assetsApi'

/**
 * A live notification says an asset changed: everything that shows it is fetched again from the API, the source of
 * truth. Shown data is refetched now, everything else when next shown. Lists and the dashboard can change with any
 * asset; details, history and assignments only with their own. These reads are background reads.
 */
export function refreshAfterChange(queryClient: QueryClient, assetId: number) {
  inBackground(() => {
    void queryClient.invalidateQueries({
      queryKey: assetsQueryKey,
      predicate: (query) => query.queryKey[1] === 'list' || query.queryKey[2] === assetId,
    })
    void queryClient.invalidateQueries({ queryKey: dashboardQueryKey })
  })
}
