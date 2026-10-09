import { fireEvent, screen, waitFor, within } from '@testing-library/react'

/** Opens an MUI select and clicks the options; a multiple select stays open, so it is closed with Escape. */
export async function choose(selectName: string, ...options: string[]) {
  fireEvent.mouseDown(screen.getByRole('combobox', { name: selectName }))
  const listbox = await screen.findByRole('listbox')
  for (const option of options) {
    fireEvent.click(within(listbox).getByRole('option', { name: option }))
  }
  if (screen.queryByRole('listbox')) {
    fireEvent.keyDown(screen.getByRole('listbox'), { key: 'Escape' })
  }
  await waitFor(() => expect(screen.queryByRole('listbox')).not.toBeInTheDocument())
}

/** The options an MUI select offers, closing it again. */
export async function optionsOf(selectName: string) {
  fireEvent.mouseDown(screen.getByRole('combobox', { name: selectName }))
  const listbox = await screen.findByRole('listbox')
  const options = within(listbox)
    .getAllByRole('option')
    .map((option) => option.textContent)
  fireEvent.keyDown(listbox, { key: 'Escape' })
  await waitFor(() => expect(screen.queryByRole('listbox')).not.toBeInTheDocument())
  return options
}
