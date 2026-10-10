// Windows ignores case in file names, and an import without an extension tries .ts before .tsx. With AssetForm.tsx
// next to assetForm.ts, '../inventory/AssetForm' found the component on Linux but the schema file on Windows, and the
// app stayed blank there ("does not provide an export named 'AssetForm'").
const modules = Object.keys(import.meta.glob(['/src/**/*.{ts,tsx,js,jsx}', '/e2e/**/*.{ts,tsx,js,jsx}']))

describe('module file names', () => {
  it('stay distinct when the case and the extension are ignored', () => {
    expect(modules.length).toBeGreaterThan(10)
    const byImportPath = new Map<string, string[]>()
    for (const path of modules) {
      const importPath = path.replace(/\.(tsx?|jsx?)$/, '').toLowerCase()
      byImportPath.set(importPath, [...(byImportPath.get(importPath) ?? []), path])
    }

    expect([...byImportPath.values()].filter((paths) => paths.length > 1)).toEqual([])
  })
})
