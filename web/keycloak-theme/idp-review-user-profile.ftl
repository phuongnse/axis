<#-- The Axis page that reviews a profile after a first brokered sign-in. It is the update profile page with another
     title. The form posts each user profile field under its attribute name. It writes Keycloak's data as JSON into the same
     element as login.ftl and the React bundle draws the page from it. The "page" field tells the bundle which page to
     draw. Keycloak escapes output as HTML, so every string goes through ?json_string?no_esc: ?json_string also escapes
     "</", which keeps the script element closed. -->
<#import "frame.ftl" as frame>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<@frame.head title=msg("axisReviewProfileTitle")/>
<body>
  <#-- Whether any field has an error. A field's error shows under the field, so the page shows only an error that
       belongs to no field above the form. -->
  <#assign fieldError = false>
  <#list profile.attributes as a>
    <#if messagesPerField.existsError(a.name)><#assign fieldError = true></#if>
  </#list>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "reviewProfile",
    "title": "${msg("axisReviewProfileTitle")?json_string?no_esc}",
    <@frame.realmName/>
    <#if message?? && message.type != 'error'>
    "notice": "${message.summary?json_string?no_esc}",
    <#elseif message?? && !fieldError>
    "error": "${message.summary?json_string?no_esc}",
    </#if>
    "loginAction": "${url.loginAction?json_string?no_esc}",
    "fields": [
      <#list profile.attributes as a>
      {
        "name": "${a.name?json_string?no_esc}",
        "label": "${advancedMsg(a.displayName!'')?json_string?no_esc}",
        <#if a.value?has_content>
        "value": "${a.value?json_string?no_esc}",
        </#if>
        <#if a.autocomplete?has_content>
        "autoComplete": "${a.autocomplete?json_string?no_esc}",
        </#if>
        <#if messagesPerField.existsError(a.name)>
        "error": "${messagesPerField.get(a.name)?json_string?no_esc}",
        </#if>
        "required": ${a.required?c},
        "readOnly": ${a.readOnly?c}
      }<#sep>,</#sep>
      </#list>
    ],
    <@frame.locales/>
    <@frame.texts/>
    "texts": {
      "submit": "${msg("axisSaveProfile")?json_string?no_esc}",
      "fieldRequired": "${msg("axisFieldRequired", "{field}")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
