<#-- The Axis page that sets a password, which an invitation's update-password action and a reset link open. It writes
     Keycloak's data as JSON into the same element as login.ftl and the React bundle draws the page from it. When an application started the step, the page shows Cancel. The "page"
     field tells the bundle which page to draw. Keycloak escapes output as HTML, so every string goes through
     ?json_string?no_esc: ?json_string also escapes "</", which keeps the script element closed. -->
<#import "frame.ftl" as frame>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<@frame.head title=msg("axisSetPasswordTitle")/>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "updatePassword",
    "title": "${msg("axisSetPasswordTitle")?json_string?no_esc}",
    <@frame.realmName/>
    <#if messagesPerField.existsError('password', 'password-confirm')>
    "error": "${messagesPerField.getFirstError('password', 'password-confirm')?json_string?no_esc}",
    <#elseif message?? && message.type == 'error'>
    "error": "${message.summary?json_string?no_esc}",
    </#if>
    "loginAction": "${url.loginAction?json_string?no_esc}",
    <#if isAppInitiatedAction??>
    "appInitiated": true,
    </#if>
    <@frame.locales/>
    <@frame.texts/>
    "texts": {
      "title": "${msg("axisSetPasswordTitle")?json_string?no_esc}",
      "message": "${msg("axisSetPasswordMessage")?json_string?no_esc}",
      "newPassword": "${msg("axisNewPassword")?json_string?no_esc}",
      "confirmPassword": "${msg("axisConfirmPassword")?json_string?no_esc}",
      "newPasswordRequired": "${msg("axisNewPasswordRequired")?json_string?no_esc}",
      "passwordsDiffer": "${msg("axisPasswordsDiffer")?json_string?no_esc}",
      "setPassword": "${msg("axisSetPassword")?json_string?no_esc}",
      "cancel": "${msg("axisCancel")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
