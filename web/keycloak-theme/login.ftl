<#-- The Axis sign-in page. It writes Keycloak's sign-in data as JSON and the React bundle draws the page from it.
     Keycloak escapes output as HTML, so every string goes through ?json_string?no_esc: ?json_string also escapes
     "</", which keeps the script element closed. Message texts take {0} because Keycloak formats them with
     MessageFormat, and the page replaces the literal {provider} that passes here. A client that carries the attribute
     axis.accounts belongs to a site, so the context then holds a "site" object with the providers (axis.providers, a
     space-separated list of aliases) and the accounts flag that the site allows. Other clients get no "site". A
     provider's display name is plain text or a realm message key such as ${axisProvider.oidc-issuer}, which activation
     writes for each language, so advancedMsg gives the label in the page's language. The "realmName" is the realm's
     display name. Keycloak's realm.displayName falls back to the realm id, so the context leaves it out when the
     display name is empty or equals the id, and the page then names Axis, never the tenant id. -->
<#import "frame.ftl" as axis>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${msg("axisPageTitle")}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "signIn",
    "title": "${msg("axisSignInTitle")?json_string?no_esc}",
    <#if (realm.displayName)?has_content && realm.displayName != realm.name>
    "realmName": "${realm.displayName?json_string?no_esc}",
    </#if>
    "providers": [
      <#if social?? && social.providers??>
        <#list social.providers as p>
      {
        "alias": "${p.alias?json_string?no_esc}",
        "label": "${advancedMsg(p.displayName)?json_string?no_esc}",
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
    <#-- Proposed: a notice that Keycloak brings back to the sign-in page, such as the one after "Send link". -->
    <#if message?? && message.type != 'error'>
    "notice": "${message.summary?json_string?no_esc}",
    </#if>
    "loginAction": "${url.loginAction?json_string?no_esc}",
    "rememberMe": ${(realm.rememberMe)?c},
    <#if realm.resetPasswordAllowed>
    "forgotPasswordUrl": "${url.loginResetCredentialsUrl?json_string?no_esc}",
    </#if>
    <@axis.preferences/>
    <@axis.legal/>
    "texts": {
      "continueWith": "${msg("axisContinueWith", "{provider}")?json_string?no_esc}",
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
