<#-- Proposed: the Axis layout of every HTML email: the organization's mark and name, one white card with the message,
     and a muted footer, in the light tokens (canvas #f5f5f5, surface #ffffff, primary #5b3fd9). Tables and inline styles
     only, because email clients drop style sheets. The name is the realm's display name, and Axis when it has none. -->
<#macro emailLayout>
<#assign organization = ((realmName)?has_content)?then(realmName, "Axis")>
<html lang="${locale.language}" dir="${(ltr)?then('ltr','rtl')}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="color-scheme" content="light">
</head>
<body style="margin:0;padding:0;background:#f5f5f5;">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f5f5f5;">
    <tr>
      <td align="center" style="padding:32px 16px;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:480px;font-family:'IBM Plex Sans',system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;color:rgba(0,0,0,0.88);font-size:14px;line-height:22px;">
          <tr>
            <td align="center" style="padding-bottom:16px;">
              <table role="presentation" cellpadding="0" cellspacing="0">
                <tr>
                  <td width="32" height="32" align="center" valign="middle" style="width:32px;height:32px;background:#5b3fd9;border-radius:6px;color:#ffffff;font-weight:600;font-size:16px;">${organization?substring(0, 1)?upper_case}</td>
                  <td style="padding-left:8px;font-size:20px;font-weight:600;">${organization}</td>
                </tr>
              </table>
            </td>
          </tr>
          <tr>
            <td style="background:#ffffff;border:1px solid #f0f0f0;border-radius:6px;padding:32px;">
              <#nested>
            </td>
          </tr>
          <tr>
            <td align="center" style="padding-top:16px;color:rgba(0,0,0,0.58);font-size:12px;line-height:20px;">
              ${msg("axisEmailFooter", organization)}<br>&copy; ${.now?string("yyyy")} Axis
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>
</#macro>

<#-- Proposed: the one solid button of an email, a table cell so that every client draws it. The link itself is in the
     plain-text part, not under the button, because an action link holds a token of some 2,000 characters. -->
<#macro button href label>
  <table role="presentation" cellpadding="0" cellspacing="0" style="margin:24px 0;">
    <tr>
      <td align="center" style="background:#5b3fd9;border-radius:6px;">
        <a href="${href}" style="display:inline-block;padding:9px 24px;color:#ffffff;text-decoration:none;font-weight:500;">${label}</a>
      </td>
    </tr>
  </table>
</#macro>
