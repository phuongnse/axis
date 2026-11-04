<#-- Proposed: the Axis notice page, such as the one after "Send link", the one before an invitation's steps and the one after an account change. A required step's link is the solid Continue button; otherwise the page links back to the application. -->
<#import "frame.ftl" as axis>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${msg("axisInfoTitle")}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "title": "<#if messageHeader??>${msg(messageHeader)?json_string?no_esc}<#else>${msg("axisInfoTitle")?json_string?no_esc}</#if>",
    <@axis.realmName/>
    <#assign summary = (message.summary)!''>
    <#if requiredActions??><#assign summary = summary + ": " + requiredActions?map(a -> msg("requiredAction." + a))?join(", ")></#if>
    "message": "${summary?json_string?no_esc}",
    "actions": [
      <#if !(skipLink??)>
        <#if pageRedirectUri?has_content>
      { "kind": "link", "label": "${msg("axisBackToApplication")?json_string?no_esc}", "href": "${pageRedirectUri?json_string?no_esc}" }
        <#elseif actionUri?has_content>
      { "kind": "link", "primary": true, "label": "${msg("axisContinue")?json_string?no_esc}", "href": "${actionUri?json_string?no_esc}" }
        <#elseif (client.baseUrl)?has_content>
      { "kind": "link", "label": "${msg("axisBackToApplication")?json_string?no_esc}", "href": "${client.baseUrl?json_string?no_esc}" }
        </#if>
      </#if>
    ],
    <@axis.preferences/>
    <@axis.legal/>
    "page": "message"
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
