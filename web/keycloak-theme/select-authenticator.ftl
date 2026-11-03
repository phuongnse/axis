<#-- The Axis page behind "Try another way", where the person picks another way to sign in. Each method is one
     row that posts its execution id as authenticationExecution. It writes Keycloak's data as JSON into the same element as login.ftl and the React bundle draws the page from
     it. The "page" field tells the bundle which page to draw. Keycloak escapes output as HTML, so every string goes
     through ?json_string?no_esc: ?json_string also escapes "</", which keeps the script element closed. -->
<#import "frame.ftl" as frame>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<@frame.head title=msg("axisChooseMethodTitle")/>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "selectAuthenticator",
    "title": "${msg("axisChooseMethodTitle")?json_string?no_esc}",
    <@frame.realmName/>
    <#if message?? && message.type == 'error'>
    "error": "${message.summary?json_string?no_esc}",
    </#if>
    "loginAction": "${url.loginAction?json_string?no_esc}",
    "restartUrl": "${url.loginRestartFlowUrl?json_string?no_esc}",
    "methods": [
      <#list auth.authenticationSelections as s>
      {
        "id": "${s.authExecId?json_string?no_esc}",
        "label": "${msg(s.displayName)?json_string?no_esc}",
        "help": "${msg(s.helpText)?json_string?no_esc}"
      }<#sep>,</#sep>
      </#list>
    ],
    <@frame.locales/>
    <@frame.texts/>
    "texts": {
      "title": "${msg("axisChooseMethodTitle")?json_string?no_esc}",
      "message": "${msg("axisChooseMethodMessage")?json_string?no_esc}",
      "backToSignIn": "${msg("axisBackToSignIn")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
