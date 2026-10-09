import ViewColumnIcon from '@mui/icons-material/ViewColumn'
import { Button, Checkbox, ListItemIcon, ListItemText, Menu, MenuItem } from '@mui/material'
import { useId, useState } from 'react'
import { assetColumns } from './columns'

interface ColumnMenuProps {
  hidden: ReadonlySet<string>
  onChange: (hidden: ReadonlySet<string>) => void
}

/** "Sütunlar": turns table columns on and off. At least one column always stays visible. */
export function ColumnMenu({ hidden, onChange }: ColumnMenuProps) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const menuId = useId()
  const visibleCount = assetColumns.length - hidden.size

  const toggle = (key: string) => {
    const next = new Set(hidden)
    if (next.has(key)) {
      next.delete(key)
    } else {
      next.add(key)
    }
    onChange(next)
  }

  return (
    <>
      <Button
        variant="outlined"
        startIcon={<ViewColumnIcon />}
        aria-controls={anchor ? menuId : undefined}
        aria-haspopup="true"
        aria-expanded={anchor ? 'true' : undefined}
        onClick={(event) => setAnchor(event.currentTarget)}
      >
        Sütunlar
      </Button>
      <Menu id={menuId} anchorEl={anchor} open={Boolean(anchor)} onClose={() => setAnchor(null)}>
        {assetColumns.map((column) => {
          const visible = !hidden.has(column.key)
          return (
            <MenuItem
              key={column.key}
              role="menuitemcheckbox"
              aria-checked={visible}
              disabled={visible && visibleCount === 1}
              onClick={() => toggle(column.key)}
              dense
            >
              <ListItemIcon>
                <Checkbox edge="start" checked={visible} tabIndex={-1} disableRipple size="small" slotProps={{ input: { 'aria-hidden': true } }} />
              </ListItemIcon>
              <ListItemText primary={column.label} />
            </MenuItem>
          )
        })}
      </Menu>
    </>
  )
}
