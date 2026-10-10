import { inflateRawSync } from 'node:zlib'

/**
 * Reads the cells of a downloaded .xlsx (a zip of XML parts) without a spreadsheet library: enough to check that the
 * file the browser saved holds the rows the screen listed. Text and numbers come back as written.
 */
export function readSheet(file: Buffer, sheetName: string): string[][] {
  const entries = unzip(file)
  const part = (name: string) => {
    const entry = entries.get(name)
    if (!entry) throw new Error(`${name} is not in the file`)
    return entry
  }

  const workbook = part('xl/workbook.xml')
  const sheet = [...workbook.matchAll(/<(?:\w+:)?sheet\b[^>]*>/g)]
    .map((m) => m[0])
    .find((tag) => attribute(tag, 'name') === sheetName)
  if (!sheet) throw new Error(`No sheet named ${sheetName}`)
  const relationId = attribute(sheet, 'r:id')
  const relation = [...part('xl/_rels/workbook.xml.rels').matchAll(/<Relationship\b[^>]*>/g)]
    .map((m) => m[0])
    .find((tag) => attribute(tag, 'Id') === relationId)
  const target = attribute(relation ?? '', 'Target') ?? ''
  const xml = part(target.startsWith('/') ? target.slice(1) : `xl/${target}`)

  return [...xml.matchAll(/<(?:\w+:)?row\b[^>]*>([\s\S]*?)<\/(?:\w+:)?row>/g)].map(([, cells]) => {
    const row: string[] = []
    for (const [, reference, body] of cells.matchAll(/<(?:\w+:)?c\b[^>]*\br="([A-Z]+)\d+"[^>]*>([\s\S]*?)<\/(?:\w+:)?c>/g)) {
      const column = [...reference].reduce((n, letter) => n * 26 + letter.charCodeAt(0) - 64, 0) - 1
      while (row.length < column) row.push('')
      const text = /<(?:\w+:)?t\b[^>]*>([\s\S]*?)<\/(?:\w+:)?t>/.exec(body) ?? /<(?:\w+:)?v>([\s\S]*?)<\/(?:\w+:)?v>/.exec(body)
      row.push(unescape(text?.[1] ?? ''))
    }
    return row
  })
}

function attribute(tag: string, name: string): string | undefined {
  return new RegExp(`\\s${name.replace(':', '\\:')}="([^"]*)"`).exec(tag)?.[1]
}

function unescape(text: string) {
  return text
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&amp;/g, '&')
}

/** The zip's entries by name, from its central directory (stored or deflated). */
function unzip(file: Buffer): Map<string, string> {
  const end = file.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]))
  if (end < 0) throw new Error('Not a zip file')
  const count = file.readUInt16LE(end + 10)
  let offset = file.readUInt32LE(end + 16)
  const entries = new Map<string, string>()
  for (let i = 0; i < count; i++) {
    const method = file.readUInt16LE(offset + 10)
    const compressedSize = file.readUInt32LE(offset + 20)
    const nameLength = file.readUInt16LE(offset + 28)
    const extraLength = file.readUInt16LE(offset + 30)
    const commentLength = file.readUInt16LE(offset + 32)
    const localHeader = file.readUInt32LE(offset + 42)
    const name = file.toString('utf8', offset + 46, offset + 46 + nameLength)
    const dataStart = localHeader + 30 + file.readUInt16LE(localHeader + 26) + file.readUInt16LE(localHeader + 28)
    const data = file.subarray(dataStart, dataStart + compressedSize)
    entries.set(name, (method === 8 ? inflateRawSync(data) : data).toString('utf8'))
    offset += 46 + nameLength + extraLength + commentLength
  }
  return entries
}
