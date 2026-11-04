<#-- Proposed: the Axis page that a sign-in step reached after it expired, such as after Back. Continue resumes the step; Start again restarts the sign-in. -->
<#import "frame.ftl" as axis>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${msg("axisPageExpiredTitle")}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "title": "${msg("axisPageExpiredTitle")?json_string?no_esc}",
    <@axis.realmName/>
    "message": "${msg("axisPageExpiredMessage")?json_string?no_esc}",
    "actions": [
      { "kind": "link", "primary": true, "label": "${msg("axisContinue")?json_string?no_esc}", "href": "${url.loginAction?json_string?no_esc}" },
      { "kind": "link", "label": "${msg("axisStartAgain")?json_string?no_esc}", "href": "${url.loginRestartFlowUrl?json_string?no_esc}" }
    ],
    <@axis.preferences/>
    <@axis.legal/>
    "page": "message"
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
