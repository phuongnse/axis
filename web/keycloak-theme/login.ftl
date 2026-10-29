<#-- The Axis sign-in page. It writes Keycloak's sign-in data as JSON and the React bundle draws the page from it.
     Keycloak escapes output as HTML, so every string goes through ?json_string?no_esc: ?json_string also escapes
     "</", which keeps the script element closed. Message texts take {0} because Keycloak formats them with
     MessageFormat, and the page replaces the literal {provider} that passes here. A client that carries the attribute
     axis.accounts belongs to a site, so the context then holds a "site" object with the providers (axis.providers, a
     space-separated list of aliases) and the accounts flag that the site allows. Other clients get no "site". -->
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${msg("axisSignInTitle")}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "title": "${msg("axisSignInTitle")?json_string?no_esc}",
    "realmName": "${((realm.displayName)?has_content?then(realm.displayName, realm.name))?json_string?no_esc}",
    "providers": [
      <#if social?? && social.providers??>
        <#list social.providers as p>
      {
        "alias": "${p.alias?json_string?no_esc}",
        "label": "${p.displayName?json_string?no_esc}",
        "loginUrl": "${p.loginUrl?json_string?no_esc}"
      }<#sep>,</#sep>
        </#list>
      </#if>
    ],
    "accounts": ${realm.password?c},
    <#assign siteAccounts = (client.attributes['axis.accounts'])!''>
    <#if siteAccounts?has_content>
    "site": {
      "providers": [<#list ((client.attributes['axis.providers'])!'')?word_list as a>"${a?json_string?no_esc}"<#sep>,</#sep></#list>],
      "accounts": ${(siteAccounts == 'true')?c}
    },
    </#if>
    "username": "${((login.username)!'')?json_string?no_esc}",
    <#if messagesPerField.existsError('username', 'password')>
    "error": "${messagesPerField.getFirstError('username', 'password')?json_string?no_esc}",
    <#elseif message?? && message.type == 'error'>
    "error": "${message.summary?json_string?no_esc}",
    </#if>
    "loginAction": "${url.loginAction?json_string?no_esc}",
    "rememberMe": ${(realm.rememberMe)?c},
    <#if realm.resetPasswordAllowed>
    "forgotPasswordUrl": "${url.loginResetCredentialsUrl?json_string?no_esc}",
    </#if>
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
      "signInWith": "${msg("axisSignInWith", "{provider}")?json_string?no_esc}",
      "providerOnly": "${msg("axisProviderOnly", "{provider}")?json_string?no_esc}",
      "or": "${msg("axisOr")?json_string?no_esc}",
      "username": "${msg("axisUsername")?json_string?no_esc}",
      "password": "${msg("axisPassword")?json_string?no_esc}",
      "usernameRequired": "${msg("axisUsernameRequired")?json_string?no_esc}",
      "passwordRequired": "${msg("axisPasswordRequired")?json_string?no_esc}",
      "forgotPassword": "${msg("axisForgotPassword")?json_string?no_esc}",
      "keepSignedIn": "${msg("axisKeepSignedIn")?json_string?no_esc}",
      "signIn": "${msg("axisSignIn")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
