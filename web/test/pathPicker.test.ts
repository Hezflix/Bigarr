import { describe, expect, test } from 'bun:test'
import { acceptedPath, matchesExtension, pathEntries } from '../src/components/pathPickerModel'

const item = (name: string, path: string) => ({ name, path, size: 0, lastModified: null })

const listing = {
  directories: [item('db', '/config/db'), item('logs', '/config/logs')],
  files: [item('db.sqlite3', '/config/db.sqlite3'), item('settings.json', '/config/settings.json'), item('tautulli.DB', '/config/tautulli.DB')],
}

describe('pathEntries', () => {
  test('a folder picker lists folders only', () => {
    expect(pathEntries(listing, '').map((e) => e.name)).toEqual(['db', 'logs'])
  })

  test('with extensions, matching files come after the folders', () => {
    const entries = pathEntries(listing, '', ['.sqlite3', '.sqlite', '.db'])
    expect(entries.map((e) => [e.kind, e.name])).toEqual([
      ['folder', 'db'],
      ['folder', 'logs'],
      ['file', 'db.sqlite3'],
      ['file', 'tautulli.DB'],
    ])
  })

  test('what is being typed filters folders and files alike', () => {
    expect(pathEntries(listing, 'DB', ['sqlite3']).map((e) => e.name)).toEqual(['db', 'db.sqlite3'])
  })
})

describe('acceptedPath', () => {
  test('a file sets its full path, no trailing separator', () => {
    expect(acceptedPath({ ...item('db.sqlite3', '/config/db/db.sqlite3'), kind: 'file' })).toBe('/config/db/db.sqlite3')
  })

  test('a folder keeps the trailing separator', () => {
    expect(acceptedPath({ ...item('db', '/config/db'), kind: 'folder' })).toBe('/config/db/')
    expect(acceptedPath({ ...item('db', 'C:\\config\\db'), kind: 'folder' })).toBe('C:\\config\\db\\')
  })
})

describe('matchesExtension', () => {
  test('ignores case and the leading dot', () => {
    expect(matchesExtension('tautulli.DB', ['db'])).toBe(true)
    expect(matchesExtension('tautulli.db-wal', ['.db'])).toBe(false)
  })
})
