<#-- The Axis page that adds an authenticator, where the person scans the QR code or types the key and enters the 6-digit
     code. It writes Keycloak's data as JSON into the same element as login.ftl and the React bundle draws the page from
     it. When an application started the step, the page shows Cancel. The "page" field tells the bundle which page to draw. Keycloak escapes output as HTML, so every string goes
     through ?json_string?no_esc: ?json_string also escapes "</", which keeps the script element closed. -->
<#import "frame.ftl" as frame>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<@frame.head title=msg("axisAddAuthenticatorTitle")/>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "addAuthenticator",
    "title": "${msg("axisAddAuthenticatorTitle")?json_string?no_esc}",
    <@frame.realmName/>
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
    <#if isAppInitiatedAction??>
    "appInitiated": true,
    </#if>
    <@frame.locales/>
    <@frame.texts/>
    "texts": {
      "title": "${msg("axisAddAuthenticatorTitle")?json_string?no_esc}",
      "message": "${msg("axisAddAuthenticatorMessage")?json_string?no_esc}",
      "qrCode": "${msg("axisQrCode")?json_string?no_esc}",
      "manualKey": "${msg("axisManualKey")?json_string?no_esc}",
      "code": "${msg("axisCode")?json_string?no_esc}",
      "codeRequired": "${msg("axisTwoStepMessage")?json_string?no_esc}",
      "deviceName": "${msg("axisDeviceName")?json_string?no_esc}",
      "deviceNameRequired": "${msg("axisDeviceNameRequired")?json_string?no_esc}",
      "submit": "${msg("axisAddAuthenticator")?json_string?no_esc}",
      "cancel": "${msg("axisCancel")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
