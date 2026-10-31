<#-- Proposed: the Axis SAML redirect. The page posts the SAML message to the organization's provider at once; Continue posts it when the browser did not. -->
<#import "frame.ftl" as axis>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${msg("axisRedirectingTitle")}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "title": "${msg("axisRedirectingTitle")?json_string?no_esc}",
    <@axis.realmName/>
    "message": "${msg("axisRedirectingMessage")?json_string?no_esc}",
    "autoSubmit": true,
    "actions": [
      { "kind": "post", "primary": true, "label": "${msg("axisContinue")?json_string?no_esc}", "href": "${samlPost.url?json_string?no_esc}",
        "fields": {
          <#assign samlFields = []>
          <#if samlPost.SAMLRequest??><#assign samlFields = samlFields + ["\"SAMLRequest\": \"" + samlPost.SAMLRequest?json_string + "\""]></#if>
          <#if samlPost.SAMLResponse??><#assign samlFields = samlFields + ["\"SAMLResponse\": \"" + samlPost.SAMLResponse?json_string + "\""]></#if>
          <#if samlPost.relayState??><#assign samlFields = samlFields + ["\"RelayState\": \"" + samlPost.relayState?json_string + "\""]></#if>
          ${samlFields?join(", ")?no_esc}
        } }
    ],
    <@axis.preferences/>
    <@axis.legal/>
    "page": "message"
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
