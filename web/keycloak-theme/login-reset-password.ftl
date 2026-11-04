<#-- The Axis "Forgot password?" page, where the person enters the email or username that Keycloak sends a link to. It
     writes Keycloak's data as JSON into the same element as login.ftl and the React bundle draws the page from it. The
     "page" field tells the bundle which page to draw. Keycloak escapes output as HTML, so every string goes through
     ?json_string?no_esc: ?json_string also escapes "</", which keeps the script element closed. -->
<#import "frame.ftl" as frame>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<@frame.head title=msg("axisResetTitle")/>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "resetPassword",
    "title": "${msg("axisResetTitle")?json_string?no_esc}",
    <@frame.realmName/>
    <#if (auth.attemptedUsername)?has_content>
    "username": "${auth.attemptedUsername?json_string?no_esc}",
    </#if>
    <#if messagesPerField.existsError('username')>
    "error": "${messagesPerField.get('username')?json_string?no_esc}",
    <#elseif message?? && message.type == 'error'>
    "error": "${message.summary?json_string?no_esc}",
    </#if>
    "loginAction": "${url.loginAction?json_string?no_esc}",
    "loginUrl": "${url.loginUrl?json_string?no_esc}",
    <@frame.locales/>
    <@frame.texts/>
    "texts": {
      "title": "${msg("axisResetTitle")?json_string?no_esc}",
      "message": "${msg("axisResetMessage")?json_string?no_esc}",
      "username": "${msg("axisUsername")?json_string?no_esc}",
      "usernameRequired": "${msg("axisUsernameRequired")?json_string?no_esc}",
      "sendLink": "${msg("axisSendLink")?json_string?no_esc}",
      "backToSignIn": "${msg("axisBackToSignIn")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
