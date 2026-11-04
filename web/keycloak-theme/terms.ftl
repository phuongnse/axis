<#-- The Axis page of Keycloak's Terms and Conditions required action, which a tenant's first sign-in opens when the tenant
     sets terms. It writes Keycloak's data as JSON into the same element as login.ftl and the React bundle draws the page
     from it. The "termsUrl" and "privacyUrl" come from the client attributes axis.tenantTermsUrl and
     axis.tenantPrivacyUrl, each left out when not set. The page posts accept or cancel to the login action. The "page"
     field tells the bundle which page to draw. Keycloak escapes output as HTML, so every string goes through
     ?json_string?no_esc: ?json_string also escapes "</", which keeps the script element closed. -->
<#import "frame.ftl" as frame>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<@frame.head title=msg("axisTermsTitle")/>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "terms",
    "title": "${msg("axisTermsTitle")?json_string?no_esc}",
    <@frame.realmName/>
    <#if message?? && message.type == 'error'>
    "error": "${message.summary?json_string?no_esc}",
    </#if>
    "loginAction": "${url.loginAction?json_string?no_esc}",
    <#assign termsUrl = ((client.attributes['axis.tenantTermsUrl'])!'')>
    <#if termsUrl?has_content>
    "termsUrl": "${termsUrl?json_string?no_esc}",
    </#if>
    <#assign privacyUrl = ((client.attributes['axis.tenantPrivacyUrl'])!'')>
    <#if privacyUrl?has_content>
    "privacyUrl": "${privacyUrl?json_string?no_esc}",
    </#if>
    <@frame.locales/>
    <@frame.texts/>
    "texts": {
      "title": "${msg("axisTermsTitle")?json_string?no_esc}",
      "accept": "${msg("axisTermsAccept", "{terms}", "{privacy}")?json_string?no_esc}",
      "acceptTermsOnly": "${msg("axisTermsAcceptTermsOnly", "{terms}")?json_string?no_esc}",
      "termsOfService": "${msg("axisTermsOfService")?json_string?no_esc}",
      "privacyPolicy": "${msg("axisPrivacyPolicy")?json_string?no_esc}",
      "required": "${msg("axisTermsRequired")?json_string?no_esc}",
      "continue": "${msg("axisContinue")?json_string?no_esc}",
      "decline": "${msg("axisTermsDecline")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
