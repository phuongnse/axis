<#-- The parts that the Axis pages of the identity provider share. Each macro writes a piece of the page: head writes the
     <head> element, and realmName, locales and texts write fields of the JSON context, each ending in a comma, so a
     template calls them before its last field. The "realmName" is the realm's display name. Keycloak's
     realm.displayName falls back to the realm id, so the context leaves it out when the display name is empty or equals
     the id, and the page then names Axis, never the tenant id. The "locales" hold each language's tag, its name in its
     own language and the url that switches to it. The "frameTexts" are the texts of the 'Language and theme' gear.
     The "legal" holds the addresses of the legal line's links, from the client attributes axis.privacyUrl, axis.termsUrl
     and axis.licencesUrl, each left out when not set or when the page has no client, and the labels of the line.
     Keycloak escapes output as HTML, so every string goes through ?json_string?no_esc: ?json_string also escapes
     "</", which keeps the script element closed. -->
<#macro head title>
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${title}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
</#macro>

<#macro realmName>
    <#if (realm.displayName)?has_content && realm.displayName != realm.name>
    "realmName": "${realm.displayName?json_string?no_esc}",
    </#if>
</#macro>

<#macro locales>
    "locale": "${((locale.currentLanguageTag)!'en')?json_string?no_esc}",
    "locales": [
      <#if locale??>
        <#list locale.supported as l>
      {
        "tag": "${l.languageTag?json_string?no_esc}",
        "label": "${l.label?json_string?no_esc}",
        "url": "${l.url?json_string?no_esc}"
      }<#sep>,</#sep>
        </#list>
      </#if>
    ],
</#macro>

<#macro texts>
    "frameTexts": {
      "languageAndTheme": "${msg("axisLanguageAndTheme")?json_string?no_esc}",
      "language": "${msg("axisLanguage")?json_string?no_esc}",
      "theme": "${msg("axisTheme")?json_string?no_esc}",
      "system": "${msg("axisThemeSystem")?json_string?no_esc}",
      "light": "${msg("axisThemeLight")?json_string?no_esc}",
      "dark": "${msg("axisThemeDark")?json_string?no_esc}"
    },
    "legal": {
        <#list ["privacyUrl", "termsUrl", "licencesUrl"] as name>
            <#assign address = ((client.attributes['axis.' + name])!'')>
            <#if address?has_content>
      "${name}": "${address?json_string?no_esc}",
            </#if>
        </#list>
      "texts": {
        "legal": "${msg("axisLegal")?json_string?no_esc}",
        "privacy": "${msg("axisPrivacy")?json_string?no_esc}",
        "terms": "${msg("axisTerms")?json_string?no_esc}",
        "licences": "${msg("axisLicences")?json_string?no_esc}"
      }
    },
</#macro>
