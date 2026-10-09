import { render } from '@testing-library/react'
import { createMemoryRouter, RouterProvider, type RouteObject } from 'react-router'
import { AppProviders } from '../app/AppProviders'
import { createQueryClient } from '../app/queryClient'
import { routes as appRoutes } from '../app/routes'
import type { CurrentUser } from '../auth/authApi'
import { currentUserQueryKey } from '../auth/useAuth'

export const testUser: CurrentUser = { userName: 'ayse.yilmaz', displayName: 'Ayşe Yılmaz', roles: ['Administrator'] }

interface RenderOptions {
  routes?: RouteObject[]
  /** The signed-in user (the default), null for a visitor without a session, undefined to ask the API. */
  user?: CurrentUser | null | undefined
}

export function renderWithRouter(initialPath = '/', { routes = appRoutes, ...options }: RenderOptions = {}) {
  const queryClient = createQueryClient()
  const user = 'user' in options ? options.user : testUser
  if (user !== undefined) {
    queryClient.setQueryData(currentUserQueryKey, user)
  }

  const router = createMemoryRouter(routes, { initialEntries: [initialPath] })
  const result = render(
    <AppProviders queryClient={queryClient}>
      <RouterProvider router={router} />
    </AppProviders>,
  )
  return { ...result, router, queryClient }
}
