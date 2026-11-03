<#-- The Axis front-channel sign-out: each client's hidden sign-out frame, then on to the redirect address. It writes
     Keycloak's data as JSON into the same element as login.ftl and the React bundle draws the page from it. The "page"
     field tells the bundle which page to draw. Keycloak escapes output as HTML, so every string goes through
     ?json_string?no_esc: ?json_string also escapes "</", which keeps the script element closed. -->
<#import "frame.ftl" as frame>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<@frame.head title=msg("axisSigningOutTitle")/>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "frontChannelSignOut",
    "title": "${msg("axisSigningOutTitle")?json_string?no_esc}",
    <@frame.realmName/>
    "clients": [
      <#list logout.clients as c>
      {
        "name": "${((c.name)!'')?json_string?no_esc}",
        "url": "${c.frontChannelLogoutUrl?json_string?no_esc}"
      }<#sep>,</#sep>
      </#list>
    ],
    <#if (logout.logoutRedirectUri)?has_content>
    "redirectUrl": "${logout.logoutRedirectUri?json_string?no_esc}",
    </#if>
    <@frame.locales/>
    <@frame.texts/>
    "texts": {
      "message": "${msg("axisSigningOutMessage")?json_string?no_esc}",
      "continue": "${msg("axisContinue")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
