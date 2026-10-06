import { Result } from 'antd'
import { Link } from 'react-router'
import { useText } from '../platform/texts'

/** Shown for an address no site or page answers. The server still serves the app with 200. */
export function NotFoundPage() {
  const t = useText()
  return (
    <div data-testid="not-found">
      <Result
        status="404"
        title={t('shell.notFound.title')}
        subTitle={t('shell.notFound.message')}
        extra={<Link to="/">{t('shell.notFound.home')}</Link>}
      />
    </div>
  )
}
