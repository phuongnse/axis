<#-- The Axis two-step verification page, where the person enters the 6-digit code that follows the password. It writes
     Keycloak's data as JSON into the same element as login.ftl and the React bundle draws the page from it. The "page"
     field tells the bundle which page to draw. Keycloak escapes output as HTML, so every string goes through
     ?json_string?no_esc: ?json_string also escapes "</", which keeps the script element closed. -->
<#import "frame.ftl" as frame>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<@frame.head title=msg("axisTwoStepTitle")/>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "twoStep",
    "title": "${msg("axisTwoStepTitle")?json_string?no_esc}",
    <@frame.realmName/>
    <#if messagesPerField.existsError('totp')>
    "error": "${messagesPerField.get('totp')?json_string?no_esc}",
    <#elseif message?? && message.type == 'error'>
    "error": "${message.summary?json_string?no_esc}",
    </#if>
    "loginAction": "${url.loginAction?json_string?no_esc}",
    "restartUrl": "${url.loginRestartFlowUrl?json_string?no_esc}",
    "tryAnotherWay": ${(auth?? && auth.showTryAnotherWayLink())?c},
    <@frame.locales/>
    <@frame.texts/>
    "texts": {
      "title": "${msg("axisTwoStepTitle")?json_string?no_esc}",
      "message": "${msg("axisTwoStepMessage")?json_string?no_esc}",
      "code": "${msg("axisCode")?json_string?no_esc}",
      "verify": "${msg("axisVerify")?json_string?no_esc}",
      "tryAnotherWay": "${msg("axisTryAnotherWay")?json_string?no_esc}",
      "backToSignIn": "${msg("axisBackToSignIn")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
