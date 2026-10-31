<#-- Proposed: the Axis front-channel sign-out page. It loads each application's sign-out address in a hidden frame, then goes on to the address after sign-out, which Continue also opens. -->
<#import "frame.ftl" as axis>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${msg("axisSigningOutTitle")}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "title": "${msg("axisSigningOutTitle")?json_string?no_esc}",
    <@axis.realmName/>
    "message": "${msg("axisSigningOutMessage")?json_string?no_esc}",
    "frames": [<#list logout.clients as c>"${c.frontChannelLogoutUrl?json_string?no_esc}"<#sep>,</#sep></#list>],
    <#if logout.logoutRedirectUri?has_content>
    "redirect": "${logout.logoutRedirectUri?json_string?no_esc}",
    "actions": [ { "kind": "link", "primary": true, "label": "${msg("axisContinue")?json_string?no_esc}", "href": "${logout.logoutRedirectUri?json_string?no_esc}" } ],
    <#else>
    "actions": [],
    </#if>
    <@axis.preferences/>
    <@axis.legal/>
    "page": "message"
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
