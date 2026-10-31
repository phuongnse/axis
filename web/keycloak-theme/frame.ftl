<#-- Proposed: what the frame around every page's card needs, which each page writes into its JSON context.
     <@axis.preferences/> writes the language and theme menu at the page's top right. It gives the page's language tag, each language the realm offers by its tag and the
     address that shows the page in it, and the menu's texts. The page names each language in that language, so a
     language needs no text of its own. Strings go through ?json_string?no_esc, as in the pages. -->
<#macro preferences>
    "preferences": {
      "locale": "${((locale.currentLanguageTag)!'en')?json_string?no_esc}",
      "locales": [
        <#if locale??>
          <#list locale.supported as l>
        {
          "tag": "${l.languageTag?json_string?no_esc}",
          "url": "${l.url?json_string?no_esc}"
        }<#sep>,</#sep>
          </#list>
        </#if>
      ],
      "texts": {
        "label": "${msg("axisLanguageAndTheme")?json_string?no_esc}",
        "language": "${msg("axisLanguage")?json_string?no_esc}",
        "theme": "${msg("axisTheme")?json_string?no_esc}",
        "system": "${msg("axisThemeSystem")?json_string?no_esc}",
        "light": "${msg("axisThemeLight")?json_string?no_esc}",
        "dark": "${msg("axisThemeDark")?json_string?no_esc}"
      }
    },
</#macro>

<#-- Proposed: <@axis.legal/> writes the legal links under the card. The site's client carries them as attributes, which
     the realm sync writes: axis.privacyUrl and axis.termsUrl, the site's or else its tenant's, left out when neither
     sets one; axis.licencesUrl, the site's Open source licences page, which also links Axis's source. Until the sync
     writes it, the licences fall back to the client's base address when it is absolute and are left out otherwise,
     never guessed. -->
<#macro legal>
    <#assign privacyUrl = (client.attributes['axis.privacyUrl'])!''>
    <#assign termsUrl = (client.attributes['axis.termsUrl'])!''>
    <#assign baseUrl = ((client.baseUrl)!'')?remove_ending('/')>
    <#assign licencesUrl = (client.attributes['axis.licencesUrl'])!(baseUrl?starts_with('http')?then(baseUrl + '/licences', ''))>
    "legal": {
      <#assign links = {}>
      <#if privacyUrl?has_content><#assign links = links + {"privacy": privacyUrl}></#if>
      <#if termsUrl?has_content><#assign links = links + {"terms": termsUrl}></#if>
      <#if licencesUrl?has_content><#assign links = links + {"licences": licencesUrl}></#if>
      "links": {
        <#list links as key, value>
        "${key}": "${value?json_string?no_esc}"<#sep>,</#sep>
        </#list>
      },
      "texts": {
        "label": "${msg("axisLegal")?json_string?no_esc}",
        "privacy": "${msg("axisPrivacy")?json_string?no_esc}",
        "terms": "${msg("axisTerms")?json_string?no_esc}",
        "licences": "${msg("axisLicences")?json_string?no_esc}"
      }
    },
</#macro>

<#-- Proposed: <@axis.realmName/> writes the realm's display name for the heading, left out when it is empty or equals
     the realm id, so the page then names Axis, never the tenant id. -->
<#macro realmName>
    <#if (realm.displayName)?has_content && realm.displayName != realm.name>
    "realmName": "${realm.displayName?json_string?no_esc}",
    </#if>
</#macro>

<#-- Proposed: <@axis.backToApplication/> writes the link back to the application that started the flow, as a message
     page's last action, when Keycloak knows the application and does not skip the link. -->
<#macro backToApplication>
    <#if !(skipLink??) && (client.baseUrl)?has_content>
      { "kind": "link", "label": "${msg("axisBackToApplication")?json_string?no_esc}", "href": "${client.baseUrl?json_string?no_esc}" }
    </#if>
</#macro>
