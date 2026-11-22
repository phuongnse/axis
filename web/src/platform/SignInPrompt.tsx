import { Modal } from 'antd'
import { startSignIn } from './api'
import { AxisButton } from './AxisButton'
import { useSignInNeeded } from './signInNeeded'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * The dialog a 401 shows. It cannot be dismissed. Only the click on **Sign in** starts sign-in, so
 * a live session at the identity provider never signs the person in without their action.
 */
export function SignInPromptDialog({ open }: { open: boolean }) {
  const t = useText()
  const { size } = useDesignTokens()
  return (
    <Modal
      open={open}
      width={size.dialog.small}
      title={t('shell.signInPrompt.title')}
      // The test id sits on the dialog, because the modal root has no size and a browser test reads it as hidden.
      modalRender={(dialog) => <div data-testid="sign-in-prompt">{dialog}</div>}
      closable={false}
      keyboard={false}
      mask={{ closable: false }}
      footer={
        <AxisButton role="finish" onClick={() => startSignIn()}>
          {t('shell.account.signIn')}
        </AxisButton>
      }
    >
      {t('shell.signInPrompt.message')}
    </Modal>
  )
}

export function SignInPrompt() {
  return <SignInPromptDialog open={useSignInNeeded()} />
}
