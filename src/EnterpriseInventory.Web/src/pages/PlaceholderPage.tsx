import { Card, CardContent } from '@mui/material'
import { PageHeader } from '../components/PageHeader'
import { EmptyState } from '../components/states/EmptyState'

interface PlaceholderPageProps {
  title: string
  description: string
  emptyTitle: string
  emptyDescription: string
}

/** Page shell for modules whose data and actions are added in later stages. */
export function PlaceholderPage({ title, description, emptyTitle, emptyDescription }: PlaceholderPageProps) {
  return (
    <>
      <PageHeader title={title} description={description} />
      <Card>
        <CardContent>
          <EmptyState title={emptyTitle} description={emptyDescription} />
        </CardContent>
      </Card>
    </>
  )
}
