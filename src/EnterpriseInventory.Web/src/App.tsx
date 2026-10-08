import { createBrowserRouter, RouterProvider } from 'react-router'
import { AppProviders } from './app/AppProviders'
import { HomePage } from './pages/HomePage'

const router = createBrowserRouter([{ path: '/', element: <HomePage /> }])

export default function App() {
  return (
    <AppProviders>
      <RouterProvider router={router} />
    </AppProviders>
  )
}
