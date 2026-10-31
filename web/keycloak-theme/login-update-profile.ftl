<#-- Proposed: the Axis page that asks a person to update their profile, Keycloak's update-profile step, from the user profile's fields. -->
<#import "frame.ftl" as axis>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${msg("axisProfileTitle")}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "title": "${msg("axisProfileTitle")?json_string?no_esc}",
    <@axis.realmName/>
    <#if message?? && message.type == 'error'>
    "error": "${message.summary?json_string?no_esc}",
    <#elseif message??>
    "notice": "${message.summary?json_string?no_esc}",
    </#if>
    "loginAction": "${url.loginAction?json_string?no_esc}",
    <#assign shown = []>
    <#assign hiddenFields = []>
    <#list profile.attributes as attribute>
      <#if attribute.name == 'locale'>
        <#assign hiddenFields = hiddenFields + [attribute]>
      <#else>
        <#assign shown = shown + [attribute]>
      </#if>
    </#list>
    "fields": [
      <#list shown as attribute>
      { "name": "${attribute.name?json_string?no_esc}", "label": "${advancedMsg(attribute.displayName!attribute.name)?json_string?no_esc}",
        "value": "${(attribute.value!'')?json_string?no_esc}", "required": ${attribute.required?c}, "readOnly": ${attribute.readOnly?c},
        "type": "<#if ((attribute.annotations.inputType)!'')?contains('email') || attribute.name == 'email'>email<#else>text</#if>"<#if messagesPerField.existsError(attribute.name)>,
        "error": "${messagesPerField.get(attribute.name)?json_string?no_esc}"</#if> }<#sep>,</#sep>
      </#list>
    ],
    "hidden": { <#if (locale.currentLanguageTag)?has_content && hiddenFields?has_content>"locale": "${locale.currentLanguageTag?json_string?no_esc}"</#if> },
    "cancellable": ${(isAppInitiatedAction??)?c},
    "texts": {
      "title": "${msg("axisProfileTitle")?json_string?no_esc}",
      "message": "${msg("axisProfileMessage")?json_string?no_esc}",
      "required": "${msg("axisFieldRequired")?json_string?no_esc}",
      "submit": "${msg("axisProfileSubmit")?json_string?no_esc}",
      "cancel": "${msg("axisCancel")?json_string?no_esc}"
    },
    <@axis.preferences/>
    <@axis.legal/>
    "page": "profile"
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
