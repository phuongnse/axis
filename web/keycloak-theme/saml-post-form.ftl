<#-- The Axis SAML redirect: a short card, and a form that the React bundle posts at once. It writes Keycloak's data as
     JSON into the same element as login.ftl and the React bundle draws the page from it. The "page" field tells the
     bundle which page to draw. Only the SAML fields that Keycloak gives are written. Keycloak escapes output as HTML,
     so every string goes through ?json_string?no_esc: ?json_string also escapes "</", which keeps the script element
     closed. -->
<#import "frame.ftl" as frame>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<@frame.head title=msg("axisRedirectTitle")/>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "samlPost",
    "title": "${msg("axisRedirectTitle")?json_string?no_esc}",
    <@frame.realmName/>
    "action": "${samlPost.url?json_string?no_esc}",
    <#if samlPost.SAMLRequest??>
    "samlRequest": "${samlPost.SAMLRequest?json_string?no_esc}",
    </#if>
    <#if samlPost.SAMLResponse??>
    "samlResponse": "${samlPost.SAMLResponse?json_string?no_esc}",
    </#if>
    <#if samlPost.relayState??>
    "relayState": "${samlPost.relayState?json_string?no_esc}",
    </#if>
    <@frame.locales/>
    <@frame.texts/>
    "texts": {
      "message": "${msg("axisRedirectMessage")?json_string?no_esc}",
      "continue": "${msg("axisContinue")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
