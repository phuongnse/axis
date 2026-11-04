<#-- Proposed: the Axis sign-out question, which Keycloak shows when a sign-out comes without an ID token hint. Sign out posts the session code and confirmLogout to Keycloak's address; the link goes back to the application. -->
<#import "frame.ftl" as axis>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${msg("axisSignOutTitle")}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "title": "${msg("axisSignOutTitle")?json_string?no_esc}",
    <@axis.realmName/>
    "message": "${msg("axisSignOutMessage")?json_string?no_esc}",
    "actions": [
      { "kind": "post", "primary": true, "label": "${msg("axisSignOut")?json_string?no_esc}", "href": "${url.logoutConfirmAction?json_string?no_esc}",
        "fields": { "session_code": "${logoutConfirm.code?json_string?no_esc}", "confirmLogout": "true" } }<#if !logoutConfirm.skipLink && (client.baseUrl)?has_content>,
      { "kind": "link", "label": "${msg("axisBackToApplication")?json_string?no_esc}", "href": "${client.baseUrl?json_string?no_esc}" }</#if>
    ],
    <@axis.preferences/>
    <@axis.legal/>
    "page": "message"
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
