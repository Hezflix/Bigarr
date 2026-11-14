import type { TFunction } from 'i18next'
import type { ExternalSignInResult } from './externalSignIn'

/** What to tell the person when a sign-in or a link did not go through; null when it did. */
export function externalSignInMessage(t: TFunction, result: ExternalSignInResult, provider: string): string | null {
  const account = 'account' in result && result.account ? result.account : provider
  switch (result.status) {
    case 'signedIn':
    case 'linked':
      return null
    case 'blocked':
      return t('externalLogin.blocked')
    case 'closed':
      return t('externalLogin.closed', { provider })
    case 'expired':
      return t('externalLogin.expired')
    case 'denied':
      return t('externalLogin.denied', { provider })
    case 'unknownAccount':
      return t('externalLogin.unknownAccount', { provider, account })
    case 'linkedElsewhere':
      return t('externalLogin.linkedElsewhere', { provider, account })
    case 'disabled':
      return t('externalLogin.disabled', { provider })
    default:
      return t('auth.unexpectedError')
  }
}
