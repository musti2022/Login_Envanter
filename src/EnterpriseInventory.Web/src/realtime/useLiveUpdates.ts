import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { assetEventNames, createInventoryConnection, type AssetNotification } from './inventoryHub'
import { refreshAfterChange } from './refreshAfterChange'

export type LiveState = 'connecting' | 'connected' | 'disconnected'

/**
 * Keeps the signed-in user's live connection to the inventory hub open while the app is shown, and refreshes what
 * an announced change affects. Returns the connection's state for the header.
 */
export function useLiveUpdates(): LiveState {
  const queryClient = useQueryClient()
  const [state, setState] = useState<LiveState>('connecting')

  useEffect(() => {
    let active = true
    const show = (next: LiveState) => {
      if (active) setState(next)
    }

    const connection = createInventoryConnection()
    for (const name of assetEventNames) {
      connection.on(name, (notification: AssetNotification) => refreshAfterChange(queryClient, notification.assetId))
    }
    connection.onclose(() => show('disconnected'))
    connection.start().then(
      () => show('connected'),
      () => show('disconnected'),
    )

    return () => {
      active = false
      void connection.stop()
    }
  }, [queryClient])

  return state
}
