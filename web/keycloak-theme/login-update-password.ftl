<#-- The Axis page that sets a password, which an invitation's update-password action and a reset link open. It writes
     Keycloak's data as JSON into the same element as login.ftl and the React bundle draws the page from it. The "page"
     field tells the bundle which page to draw. Keycloak escapes output as HTML, so every string goes through
     ?json_string?no_esc: ?json_string also escapes "</", which keeps the script element closed. -->
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${msg("axisSetPasswordTitle")}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "updatePassword",
    "title": "${msg("axisSetPasswordTitle")?json_string?no_esc}",
    "realmName": "${((realm.displayName)?has_content?then(realm.displayName, realm.name))?json_string?no_esc}",
    <#if messagesPerField.existsError('password', 'password-confirm')>
    "error": "${messagesPerField.getFirstError('password', 'password-confirm')?json_string?no_esc}",
    <#elseif message?? && message.type == 'error'>
    "error": "${message.summary?json_string?no_esc}",
    </#if>
    "loginAction": "${url.loginAction?json_string?no_esc}",
    "locales": [
      <#if locale??>
        <#list locale.supported as l>
      {
        "label": "${l.label?json_string?no_esc}",
        "url": "${l.url?json_string?no_esc}"
      }<#sep>,</#sep>
        </#list>
      </#if>
    ],
    "texts": {
      "title": "${msg("axisSetPasswordTitle")?json_string?no_esc}",
      "message": "${msg("axisSetPasswordMessage")?json_string?no_esc}",
      "newPassword": "${msg("axisNewPassword")?json_string?no_esc}",
      "confirmPassword": "${msg("axisConfirmPassword")?json_string?no_esc}",
      "newPasswordRequired": "${msg("axisNewPasswordRequired")?json_string?no_esc}",
      "passwordsDiffer": "${msg("axisPasswordsDiffer")?json_string?no_esc}",
      "setPassword": "${msg("axisSetPassword")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
