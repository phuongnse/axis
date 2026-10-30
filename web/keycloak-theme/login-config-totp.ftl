<#-- The Axis page that adds an authenticator, where the person scans the QR code or types the key and enters the 6-digit
     code. It writes Keycloak's data as JSON into the same element as login.ftl and the React bundle draws the page from
     it. The "page" field tells the bundle which page to draw. Keycloak escapes output as HTML, so every string goes
     through ?json_string?no_esc: ?json_string also escapes "</", which keeps the script element closed. -->
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${msg("axisAddAuthenticatorTitle")}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "addAuthenticator",
    "title": "${msg("axisAddAuthenticatorTitle")?json_string?no_esc}",
    <#if (realm.displayName)?has_content && realm.displayName != realm.name>
    "realmName": "${realm.displayName?json_string?no_esc}",
    </#if>
    <#if messagesPerField.existsError('totp','userLabel')>
    "error": "${messagesPerField.getFirstError('totp','userLabel')?json_string?no_esc}",
    <#elseif message?? && message.type == 'error'>
    "error": "${message.summary?json_string?no_esc}",
    </#if>
    "loginAction": "${url.loginAction?json_string?no_esc}",
    "qrCode": "${totp.totpSecretQrCode?json_string?no_esc}",
    "manualKey": "${totp.totpSecretEncoded?json_string?no_esc}",
    "secret": "${totp.totpSecret?json_string?no_esc}",
    <#if mode??>
    "mode": "${mode?json_string?no_esc}",
    </#if>
    "deviceNameRequired": ${(totp.otpCredentials?size gte 1)?c},
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
      "title": "${msg("axisAddAuthenticatorTitle")?json_string?no_esc}",
      "message": "${msg("axisAddAuthenticatorMessage")?json_string?no_esc}",
      "qrCode": "${msg("axisQrCode")?json_string?no_esc}",
      "manualKey": "${msg("axisManualKey")?json_string?no_esc}",
      "code": "${msg("axisCode")?json_string?no_esc}",
      "codeRequired": "${msg("axisTwoStepMessage")?json_string?no_esc}",
      "deviceName": "${msg("axisDeviceName")?json_string?no_esc}",
      "deviceNameRequired": "${msg("axisDeviceNameRequired")?json_string?no_esc}",
      "submit": "${msg("axisAddAuthenticator")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
