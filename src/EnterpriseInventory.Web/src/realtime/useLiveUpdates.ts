import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { reportUnauthorized } from '../api/http'
import { assetEventNames, createInventoryConnection, type AssetNotification } from './inventoryHub'
import { reconnectDelay } from './reconnect'
import { refreshAfterChange, refreshEverything } from './refreshAfterChange'

/**
 * connecting: the first try is under way. connected: changes show up on their own. reconnecting: the connection
 * could not be opened or was lost, and is being tried again. disconnected: the session has ended.
 */
export type LiveState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

/**
 * Keeps the signed-in user's live connection to the inventory hub open while the app is shown, and refreshes what
 * an announced change affects. A connection that cannot be opened or is lost is tried again, without end; once it is
 * back, everything cached is fetched again, since changes made meanwhile were not announced to this screen. Returns
 * the connection's state for the header.
 */
export function useLiveUpdates(): LiveState {
  const queryClient = useQueryClient()
  const [state, setState] = useState<LiveState>('connecting')

  useEffect(() => {
    let active = true
    let failedTries = 0
    let wasConnected = false
    let retry: ReturnType<typeof setTimeout> | undefined

    const show = (next: LiveState) => {
      if (active) setState(next)
    }

    const tryLater = () => {
      show('reconnecting')
      retry = setTimeout(connect, reconnectDelay(failedTries))
    }

    function connect() {
      retry = undefined
      connection.start().then(
        () => {
          if (!active) return
          const missedChanges = wasConnected || failedTries > 0
          failedTries = 0
          wasConnected = true
          show('connected')
          if (missedChanges) {
            refreshEverything(queryClient)
          }
        },
        () => {
          if (!active) return
          failedTries++
          tryLater()
        },
      )
    }

    const connection = createInventoryConnection({
      // Trying again cannot help; the app sends the user to sign in, which ends this hook.
      onUnauthorized: () => {
        show('disconnected')
        active = false
        clearTimeout(retry)
        reportUnauthorized()
      },
    })
    for (const name of assetEventNames) {
      connection.on(name, (notification: AssetNotification) => refreshAfterChange(queryClient, notification.assetId))
    }
    connection.onclose(() => {
      if (active) tryLater()
    })
    connect()

    return () => {
      active = false
      clearTimeout(retry)
      void connection.stop()
    }
  }, [queryClient])

  return state
}
