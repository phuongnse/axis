<#-- The Axis error page: Keycloak's message as an error alert, with the client's home address when it has one. It
     writes Keycloak's data as JSON into the same element as login.ftl and the React bundle draws the page from it. The
     "page" field tells the bundle which page to draw. The client can be missing on an error page, so every read of it
     is guarded. Keycloak escapes output as HTML, so every string goes through ?json_string?no_esc: ?json_string also
     escapes "</", which keeps the script element closed. -->
<#import "frame.ftl" as frame>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<@frame.head title=msg("axisErrorTitle")/>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "error",
    "title": "${msg("axisErrorTitle")?json_string?no_esc}",
    <@frame.realmName/>
    "error": "${((message.summary)!msg("axisErrorTitle"))?json_string?no_esc}",
    <#if !skipLink?? && (client.baseUrl)?has_content>
    "backUrl": "${client.baseUrl?json_string?no_esc}",
    </#if>
    <@frame.locales/>
    <@frame.texts/>
    "texts": {
      "backToApplication": "${msg("axisBackToApplication")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
