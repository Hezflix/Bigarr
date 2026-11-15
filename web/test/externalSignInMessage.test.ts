import { describe, expect, test } from 'bun:test'
import type { TFunction } from 'i18next'
import { externalSignInMessage } from '../src/features/auth/externalSignInMessage'

const t = ((key: string, values?: Record<string, string>) => `${key} ${JSON.stringify(values ?? {})}`) as unknown as TFunction

describe('externalSignInMessage', () => {
  test('a sign-in or a link that went through says nothing', () => {
    expect(externalSignInMessage(t, { status: 'signedIn', account: 'demo' }, 'Plex')).toBeNull()
    expect(externalSignInMessage(t, { status: 'linked', account: 'demo' }, 'Plex')).toBeNull()
  })

  test('an unknown account is named, falling back to the provider', () => {
    expect(externalSignInMessage(t, { status: 'unknownAccount', account: 'demo-stranger' }, 'Plex')).toBe(
      'externalLogin.unknownAccount {"provider":"Plex","account":"demo-stranger"}',
    )
    expect(externalSignInMessage(t, { status: 'unknownAccount', account: null }, 'Plex')).toContain('"account":"Plex"')
  })

  test('the popup closed or blocked gets its own message', () => {
    expect(externalSignInMessage(t, { status: 'closed' }, 'Plex')).toStartWith('externalLogin.closed')
    expect(externalSignInMessage(t, { status: 'blocked' }, 'Plex')).toStartWith('externalLogin.blocked')
  })
})
