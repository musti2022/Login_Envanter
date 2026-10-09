import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { fetchCurrentUser, signIn, signOut, type CurrentUser } from './authApi'

export const currentUserQueryKey = ['auth', 'currentUser'] as const
const sessionEndedQueryKey = ['auth', 'sessionEnded'] as const

export function useCurrentUser() {
  return useQuery({
    queryKey: currentUserQueryKey,
    queryFn: ({ signal }) => fetchCurrentUser(signal),
    staleTime: 60_000,
    retry: false,
  })
}

export function useSignIn() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: signIn,
    onSuccess: (user) => {
      queryClient.setQueryData(sessionEndedQueryKey, false)
      queryClient.setQueryData<CurrentUser | null>(currentUserQueryKey, user)
    },
  })
}

export function useSignOut() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: signOut,
    // Signed out locally even if the request failed: the next request would find no session anyway.
    onSettled: () => forgetUser(queryClient, false),
  })
}

/** Whether the user was sent to the sign-in page because their session ended. */
export function useSessionEnded() {
  return (
    useQuery({ queryKey: sessionEndedQueryKey, queryFn: () => false, staleTime: Infinity, initialData: false }).data ??
    false
  )
}

/** Drops everything cached for the user, so no data of theirs stays on screen after the session ends. */
export function forgetUser(queryClient: QueryClient, sessionEnded: boolean) {
  queryClient.removeQueries({ predicate: (query) => query.queryKey[0] !== 'auth' })
  queryClient.setQueryData(sessionEndedQueryKey, sessionEnded)
  queryClient.setQueryData<CurrentUser | null>(currentUserQueryKey, null)
}
