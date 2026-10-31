<#-- Proposed: the language and theme menu below the card, which every page of the theme writes into its JSON context
     with <@axis.preferences/>. It gives the page's language tag, each language the realm offers by its tag and the
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
