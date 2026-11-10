import { required } from './server-settings'

interface MailpitMessage {
  ID: string
  To: { Address: string }[]
}

const timeoutMs = 60_000
const pollMs = 500

/** The plain text of the first email in the run's Mailpit that is addressed to `address`. It waits up to a minute for the email to arrive. */
export async function emailTextTo(address: string): Promise<string> {
  const mailpitUrl = required('AXIS_E2E_MAILPIT_URL', 'the address of the Mailpit API')
  const deadline = Date.now() + timeoutMs
  for (;;) {
    const list = await fetch(`${mailpitUrl}/api/v1/messages?limit=1000`)
    if (!list.ok) throw new Error(`Listing the emails in Mailpit failed with HTTP ${list.status}.`)
    const { messages } = (await list.json()) as { messages: MailpitMessage[] }
    const message = messages.find((candidate) => candidate.To.some((to) => to.Address === address))
    if (message) {
      const response = await fetch(`${mailpitUrl}/api/v1/message/${message.ID}`)
      if (!response.ok)
        throw new Error(`Reading the email ${message.ID} in Mailpit failed with HTTP ${response.status}.`)
      return ((await response.json()) as { Text: string }).Text
    }
    if (Date.now() >= deadline)
      throw new Error(`No email to ${address} reached Mailpit within ${timeoutMs / 1000} seconds.`)
    await new Promise((resolve) => setTimeout(resolve, pollMs))
  }
}
