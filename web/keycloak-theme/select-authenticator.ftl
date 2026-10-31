<#-- Proposed: the Axis page that "Try another way" opens: each way to sign in that Keycloak offers, by its name and help line. A button posts authenticationExecution. -->
<#import "frame.ftl" as axis>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${msg("axisChooseTitle")}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "title": "${msg("axisChooseTitle")?json_string?no_esc}",
    <@axis.realmName/>
    "loginAction": "${url.loginAction?json_string?no_esc}",
    "methods": [
      <#list auth.authenticationSelections as m>
      { "id": "${m.authExecId?json_string?no_esc}", "label": "${msg(m.displayName)?json_string?no_esc}", "help": "${msg(m.helpText)?json_string?no_esc}" }<#sep>,</#sep>
      </#list>
    ],
    <@axis.preferences/>
    <@axis.legal/>
    "page": "chooseMethod"
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
