<#-- Proposed: the Axis terms page, Keycloak's "Terms and Conditions" required action. Keycloak shows it after the first
     sign-in, and again when the tenant's terms change version, until the person accepts. It writes Keycloak's data as
     JSON into the same element as login.ftl and the React bundle draws the page from it. Accepting posts `accept`;
     Keycloak then records the time in the user's terms_and_conditions attribute. Declining posts `cancel`, which ends
     the sign-in. Strings go through ?json_string?no_esc, as in the other pages. -->
<#import "frame.ftl" as axis>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${msg("axisTermsTitle")}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/assets/axis-sign-in.css">
</head>
<body>
  <div id="root"></div>
  <script type="application/json" id="axis-sign-in-context">
  {
    "page": "terms",
    "title": "${msg("axisTermsTitle")?json_string?no_esc}",
    <#if (realm.displayName)?has_content && realm.displayName != realm.name>
    "realmName": "${realm.displayName?json_string?no_esc}",
    </#if>
    <#if message?? && message.type == 'error'>
    "error": "${message.summary?json_string?no_esc}",
    </#if>
    "loginAction": "${url.loginAction?json_string?no_esc}",
    <#-- Proposed: a person accepts the tenant's terms, which the realm asks of everyone, so the realm sync writes the
         tenant's links on every client as axis.tenantPrivacyUrl and axis.tenantTermsUrl, and turns the action on only
         while the tenant has at least one. A site's own links change only the legal line under the card. -->
    <#assign tenantPrivacyUrl = (client.attributes['axis.tenantPrivacyUrl'])!''>
    <#assign tenantTermsUrl = (client.attributes['axis.tenantTermsUrl'])!''>
    "accept": {
      <#if tenantPrivacyUrl?has_content>
      "privacy": "${tenantPrivacyUrl?json_string?no_esc}"<#if tenantTermsUrl?has_content>,</#if>
      </#if>
      <#if tenantTermsUrl?has_content>
      "terms": "${tenantTermsUrl?json_string?no_esc}"
      </#if>
    },
    <@axis.preferences/>
    <@axis.legal/>
    "texts": {
      "title": "${msg("axisTermsTitle")?json_string?no_esc}",
      "message": "${msg("axisTermsMessage")?json_string?no_esc}",
      "agreeBoth": "${msg("axisTermsAgreeBoth", "{terms}", "{privacy}")?json_string?no_esc}",
      "agreeTerms": "${msg("axisTermsAgreeTerms", "{terms}")?json_string?no_esc}",
      "agreePrivacy": "${msg("axisTermsAgreePrivacy", "{privacy}")?json_string?no_esc}",
      "termsLink": "${msg("axisTermsLink")?json_string?no_esc}",
      "privacyLink": "${msg("axisPrivacyLink")?json_string?no_esc}",
      "agreeRequired": "${msg("axisTermsAgreeRequired")?json_string?no_esc}",
      "accept": "${msg("axisTermsAccept")?json_string?no_esc}",
      "decline": "${msg("axisTermsDecline")?json_string?no_esc}"
    }
  }
  </script>
  <script type="module" src="${url.resourcesPath}/assets/axis-sign-in.js"></script>
</body>
</html>
