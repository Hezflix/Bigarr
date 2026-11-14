import { externalLogin, type ExternalFlowPoll, type ExternalFlowStarted } from '@/api/client'

const POLL_MS = 1500

/** What ended the sign-in: the server's answer, or the popup closed / blocked before an answer came. */
export type ExternalSignInResult = ExternalFlowPoll | { status: 'closed' } | { status: 'blocked' }

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))

/**
 * Runs a start-then-poll sign-in in a popup. The popup is opened before the first await so the browser treats it as
 * the click's own window (popup blockers allow it), then sent to the provider's page once the server answers.
 * Polls until the server says the flow ended, or the popup closes (one last poll, then the flow is cancelled).
 */
export async function runExternalSignIn(start: () => Promise<ExternalFlowStarted>): Promise<ExternalSignInResult> {
  const popup = window.open('', 'bigarr-external-signin', 'popup,width=600,height=720')
  if (!popup) return { status: 'blocked' }

  let flow: ExternalFlowStarted
  try {
    flow = await start()
  } catch (error) {
    popup.close()
    throw error
  }

  if (!flow.authUrl) {
    popup.close()
    throw new Error('The provider gave no page to open.')
  }
  popup.location.href = flow.authUrl

  const deadline = new Date(flow.expiresAt).getTime()
  try {
    while (Date.now() < deadline) {
      await sleep(POLL_MS)
      const closed = popup.closed
      const answer = await externalLogin.poll(flow.flowId)
      if (answer.status !== 'pending') return answer
      if (closed) {
        await externalLogin.cancel(flow.flowId).catch(() => undefined)
        return { status: 'closed' }
      }
    }
    return { status: 'expired', account: null }
  } finally {
    if (!popup.closed) popup.close()
  }
}
