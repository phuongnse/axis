<#-- The layout of every page of the sample company's sign-in: a forest-green brand panel on the start side and the page
     on a cream panel, in the company's own type and colours, unlike Axis's centred card. -->
<#macro registrationLayout bodyClass="" displayInfo=false displayMessage=true displayRequiredFields=false>
<!DOCTYPE html>
<html lang="${(locale.currentLanguageTag)!'en'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="robots" content="noindex, nofollow">
  <title>${(realm.displayName)!'Example Company'}</title>
  <link rel="stylesheet" href="${url.resourcesPath}/css/company.css">
</head>
<body>
  <main class="company">
    <aside class="company-brand">
      <div class="company-mark" aria-hidden="true">EC</div>
      <p class="company-name">${(realm.displayName)!'Example Company'}</p>
      <p class="company-tagline">${msg("companyTagline")}</p>
      <p class="company-help">${msg("companyHelp")}</p>
    </aside>
    <section class="company-page">
      <div class="company-panel">
        <h1 class="company-title"><#nested "header"></h1>
        <#if displayMessage && message?has_content && (message.type != 'warning' || !isAppInitiatedAction??)>
          <div class="company-alert company-alert-${message.type}" role="alert">${kcSanitize(message.summary)?no_esc}</div>
        </#if>
        <#nested "form">
        <#if displayInfo><div class="company-info"><#nested "info"></div></#if>
      </div>
    </section>
  </main>
</body>
</html>
</#macro>
