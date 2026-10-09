import { useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { onUnauthorized } from '../api/http'
import { forgetUser } from './useAuth'

/** When any request finds the session ended (timeout, sign-out elsewhere, lost access), signs the user out here too. */
export function SessionWatcher() {
  const queryClient = useQueryClient()
  useEffect(() => onUnauthorized(() => forgetUser(queryClient, true)), [queryClient])
  return null
}
