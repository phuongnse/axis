import { Button, Modal } from 'antd'
import { startSignIn } from './api'
import { useSignInNeeded } from './signInNeeded'
import { useText } from './texts'

/**
 * The dialog a 401 shows. It cannot be dismissed. Only the click on **Sign in** starts sign-in, so
 * a live session at the identity provider never signs the person in without their action.
 */
export function SignInPrompt({
  open,
  getContainer,
}: {
  /** Opens the dialog whatever the session, as a design-system preview draws it. */
  open?: boolean
  /** Where the dialog renders, for a preview that draws it inside its own frame. */
  getContainer?: () => HTMLElement
} = {}) {
  const t = useText()
  const needed = useSignInNeeded()
  return (
    <Modal
      open={open ?? needed}
      getContainer={getContainer}
      // Proposed: the dialog says the session has ended, which is what a 401 after sign-in means, before it asks to sign in again.
      title={t('shell.signInPrompt.title')}
      // The test id sits on the dialog, because the modal root has no size and a browser test reads it as hidden.
      modalRender={(dialog) => <div data-testid="sign-in-prompt">{dialog}</div>}
      closable={false}
      keyboard={false}
      mask={{ closable: false }}
      footer={
        <Button type="primary" onClick={startSignIn}>
          {t('shell.account.signIn')}
        </Button>
      }
    >
      {t('shell.signInPrompt.message')}
    </Modal>
  )
}
