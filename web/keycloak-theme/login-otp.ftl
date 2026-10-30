<#-- The Axis two-step verification page, where the person enters the 6-digit code that follows the password. It writes
     Keycloak's data as JSON into the same element as login.ftl and the React bundle draws the page from it. The "page"
     field tells the bundle which page to draw. Keycloak escapes output as HTML, so every string goes through
     ?json_string?no_esc: ?json_string also escapes "</", which keeps the script element closed. -->
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${msg("axisTwoStepTitle")}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "twoStep",
    "title": "${msg("axisTwoStepTitle")?json_string?no_esc}",
    <#if (realm.displayName)?has_content && realm.displayName != realm.name>
    "realmName": "${realm.displayName?json_string?no_esc}",
    </#if>
    <#if messagesPerField.existsError('totp')>
    "error": "${messagesPerField.get('totp')?json_string?no_esc}",
    <#elseif message?? && message.type == 'error'>
    "error": "${message.summary?json_string?no_esc}",
    </#if>
    "loginAction": "${url.loginAction?json_string?no_esc}",
    "restartUrl": "${url.loginRestartFlowUrl?json_string?no_esc}",
    "tryAnotherWay": ${(auth?? && auth.showTryAnotherWayLink())?c},
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
