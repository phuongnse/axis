<#ftl output_format="HTML">
<#-- The Axis frame of every email the identity provider sends: the organization's mark and name, one white card and a
     muted footer. Mail clients ignore style sheets and most CSS, so it is tables and inline styles, in the light
     tokens of web/src/platform/designTokens.ts. A mail client cannot draw rgba, so the text colours are what the
     tokens give over white and over the canvas. The macro passes the name that the frame shows to its content, which
     is Axis when the realm has no display name: Keycloak's realmName is then the realm id, and the id is also the
     segment after /realms/ in the link of every action email. -->
<#macro emailLayout>
<#assign realmId = (link!"")?keep_after("/realms/")?keep_before("/")>
<#assign name = (realmName?has_content && realmName != realmId)?then(realmName, "Axis")>
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${name}</title>
</head>
<body style="margin:0;padding:0;background-color:#f5f5f5;">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#f5f5f5;">
  <tr>
    <td align="center" style="padding:32px 16px;">
      <table role="presentation" width="480" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:480px;font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;font-size:14px;line-height:22px;color:#1f1f1f;">
        <tr>
          <td style="padding-bottom:16px;">
            <table role="presentation" cellpadding="0" cellspacing="0" border="0">
              <tr>
                <td width="32" height="32" align="center" valign="middle" bgcolor="#5b3fd9" style="width:32px;height:32px;border-radius:6px;background-color:#5b3fd9;color:#ffffff;font-size:16px;font-weight:600;line-height:32px;">${name?substring(0, 1)?upper_case}</td>
                <td style="padding-left:12px;font-size:16px;font-weight:600;color:#1f1f1f;">${name}</td>
              </tr>
            </table>
          </td>
        </tr>
        <tr>
          <td bgcolor="#ffffff" style="padding:32px;background-color:#ffffff;border:1px solid #f0f0f0;border-radius:6px;">
            <#nested name>
          </td>
        </tr>
        <tr>
          <td style="padding-top:16px;font-size:12px;line-height:20px;color:#666666;">${msg("axisEmailFooter")}</td>
        </tr>
      </table>
    </td>
  </tr>
</table>
</body>
</html>
</#macro>

<#-- The heading of the card. -->
<#macro axisTitle>
<h1 style="margin:0 0 16px 0;font-size:20px;line-height:28px;font-weight:600;color:#1f1f1f;"><#nested></h1>
</#macro>

<#-- A paragraph of the card. -->
<#macro axisText>
<p style="margin:0 0 16px 0;"><#nested></p>
</#macro>

<#-- The card's one button, a link in a cell that carries the colour, as mail clients draw a cell's background best. -->
<#macro axisButton href label>
<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 16px 0;">
  <tr>
    <td bgcolor="#5b3fd9" style="border-radius:6px;background-color:#5b3fd9;"><a href="${href}" style="display:inline-block;padding:8px 16px;font-size:14px;line-height:22px;font-weight:600;color:#ffffff;text-decoration:none;">${label}</a></td>
  </tr>
</table>
</#macro>
