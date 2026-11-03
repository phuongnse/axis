<#-- The Axis sign-out question, which Keycloak shows only when the server could not end the session itself. It writes
     Keycloak's data as JSON into the same element as login.ftl and the React bundle draws the page from it. The "page"
     field tells the bundle which page to draw. "backUrl" is the client's home address, left out when Keycloak says to
     skip the link. Keycloak escapes output as HTML, so every string goes through ?json_string?no_esc: ?json_string
     also escapes "</", which keeps the script element closed. -->
<#import "frame.ftl" as frame>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<@frame.head title=msg("axisSignOutTitle")/>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "signOut",
    "title": "${msg("axisSignOutTitle")?json_string?no_esc}",
    <@frame.realmName/>
    "action": "${url.logoutConfirmAction?json_string?no_esc}",
    "sessionCode": "${logoutConfirm.code?json_string?no_esc}",
    <#if !logoutConfirm.skipLink && (client.baseUrl)?has_content>
    "backUrl": "${client.baseUrl?json_string?no_esc}",
    </#if>
    <@frame.locales/>
    <@frame.texts/>
    "texts": {
      "message": "${msg("axisSignOutMessage")?json_string?no_esc}",
      "signOut": "${msg("axisSignOut")?json_string?no_esc}",
      "backToApplication": "${msg("axisBackToApplication")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
