<#-- Proposed: the Axis error page, such as an expired invitation link or a refused brokered sign-in. Keycloak's message shows as an error alert, with the link back to the application. -->
<#import "frame.ftl" as axis>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${msg("axisErrorTitle")}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "title": "${msg("axisErrorTitle")?json_string?no_esc}",
    <@axis.realmName/>
    "message": "${((message.summary)!'')?json_string?no_esc}",
    "tone": "error",
    "actions": [
      <@axis.backToApplication/>
    ],
    <@axis.preferences/>
    <@axis.legal/>
    "page": "message"
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
