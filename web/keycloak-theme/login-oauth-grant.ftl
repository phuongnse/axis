<#-- The Axis page of Keycloak's consent step, which a client that requires consent opens before it gets a code. It writes
     Keycloak's data as JSON into the same element as login.ftl and the React bundle draws the page from it. The
     "clientName" is the client's name, or its id when it has no name, and "homeUrl" is the client's home page, left out
     when not set. Each of "scopes" is the consent text of a scope the client asks for, which advancedMsg resolves from a
     message key such as ${axisAuthoringScopeConsentText}. The page posts the "code" with accept or cancel to the consent
     address. The "page" field tells the bundle which page to draw. Keycloak escapes output as HTML, so every string goes
     through ?json_string?no_esc: ?json_string also escapes "</", which keeps the script element closed. -->
<#import "frame.ftl" as frame>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<@frame.head title=msg("axisConsentTitle")/>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "consent",
    "title": "${msg("axisConsentTitle")?json_string?no_esc}",
    <@frame.realmName/>
    <#if message?? && message.type == 'error'>
    "error": "${message.summary?json_string?no_esc}",
    </#if>
    "oauthAction": "${url.oauthAction?json_string?no_esc}",
    "code": "${oauth.code?json_string?no_esc}",
    <#if client.name?has_content>
    "clientName": "${advancedMsg(client.name)?json_string?no_esc}",
    <#else>
    "clientName": "${client.clientId?json_string?no_esc}",
    </#if>
    <#if (client.baseUrl)?has_content>
    "homeUrl": "${client.baseUrl?json_string?no_esc}",
    </#if>
    "scopes": [
      <#list oauth.clientScopesRequested as s>
      "${advancedMsg(s.consentScreenText)?json_string?no_esc}"<#sep>,</#sep>
      </#list>
    ],
    <@frame.locales/>
    <@frame.texts/>
    "texts": {
      "title": "${msg("axisConsentTitle")?json_string?no_esc}",
      "intro": "${msg("axisConsentIntro", "{client}")?json_string?no_esc}",
      "allow": "${msg("axisConsentAllow")?json_string?no_esc}",
      "deny": "${msg("axisConsentDeny")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
