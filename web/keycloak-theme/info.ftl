<#-- The Axis notice page, such as "Your account has been updated", with the link onward. It writes Keycloak's data as
     JSON into the same element as login.ftl and the React bundle draws the page from it. The "page" field tells the
     bundle which page to draw. The title is Keycloak's message header when it gives one, and "Notice" otherwise. The
     link is picked as Keycloak's info.ftl picks it: pageRedirectUri, then actionUri, then the client's home address,
     and none when skipLink is set. Keycloak escapes output as HTML, so every string goes through ?json_string?no_esc:
     ?json_string also escapes "</", which keeps the script element closed. -->
<#import "frame.ftl" as frame>
<#if messageHeader??>
  <#assign noticeTitle = msg(messageHeader)>
<#else>
  <#assign noticeTitle = msg("axisNoticeTitle")>
</#if>
<#assign noticeMessage = (message.summary)!''>
<#if requiredActions??>
  <#assign actions = []>
  <#list requiredActions as action>
    <#assign actions += [msg("requiredAction." + action)]>
  </#list>
  <#assign noticeMessage = noticeMessage + ": " + actions?join(", ")>
</#if>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<@frame.head title=noticeTitle/>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "notice",
    "title": "${noticeTitle?json_string?no_esc}",
    <@frame.realmName/>
    <#if !skipLink??>
      <#if pageRedirectUri?has_content>
    "linkUrl": "${pageRedirectUri?json_string?no_esc}",
    "linkText": "${msg("axisBackToApplication")?json_string?no_esc}",
      <#elseif actionUri?has_content>
    "linkUrl": "${actionUri?json_string?no_esc}",
    "linkText": "${msg("axisProceed")?json_string?no_esc}",
      <#elseif (client.baseUrl)?has_content>
    "linkUrl": "${client.baseUrl?json_string?no_esc}",
    "linkText": "${msg("axisBackToApplication")?json_string?no_esc}",
      </#if>
    </#if>
    <@frame.locales/>
    <@frame.texts/>
    "message": "${noticeMessage?json_string?no_esc}"
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
