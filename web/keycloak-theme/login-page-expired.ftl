<#-- The Axis expired page, which says that the page took too long and offers Continue and Start again. It writes
     Keycloak's data as JSON into the same element as login.ftl and the React bundle draws the page from it. The "page"
     field tells the bundle which page to draw. Keycloak escapes output as HTML, so every string goes through
     ?json_string?no_esc: ?json_string also escapes "</", which keeps the script element closed. -->
<#import "frame.ftl" as frame>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<@frame.head title=msg("axisPageExpiredTitle")/>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "pageExpired",
    "title": "${msg("axisPageExpiredTitle")?json_string?no_esc}",
    <@frame.realmName/>
    "continueUrl": "${url.loginAction?json_string?no_esc}",
    "restartUrl": "${url.loginRestartFlowUrl?json_string?no_esc}",
    <@frame.locales/>
    <@frame.texts/>
    "texts": {
      "message": "${msg("axisPageExpiredMessage")?json_string?no_esc}",
      "continue": "${msg("axisContinue")?json_string?no_esc}",
      "startAgain": "${msg("axisStartAgain")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
