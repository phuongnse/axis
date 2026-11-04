<#-- The sample company's sign-in form: username or email, password and one button.
     Proposed (design review 2026-10-30-studio): an empty field shows its own message under it before the form is sent,
     and Keycloak's refusal shows once above the fields with both fields marked, never saying which one was wrong. -->
<#import "template.ftl" as layout>
<#assign refused = messagesPerField.existsError('username','password')>
<@layout.registrationLayout displayMessage=true; section>
  <#if section = "header">
    ${msg("companyWelcome")}
  <#elseif section = "form">
    <form id="company-sign-in" class="company-form" action="${url.loginAction}" method="post" novalidate>
      <label class="company-label" for="username">${msg("usernameOrEmail")}</label>
      <input class="company-input" id="username" name="username" type="text" value="${(login.username!'')}" autofocus
             autocomplete="username" required <#if refused>aria-invalid="true" aria-describedby="company-alert"</#if>>
      <p class="company-error" id="username-error" hidden>${msg("companyUsernameRequired")}</p>
      <label class="company-label" for="password">${msg("password")}</label>
      <input class="company-input" id="password" name="password" type="password" autocomplete="current-password"
             required <#if refused>aria-invalid="true" aria-describedby="company-alert"</#if>>
      <p class="company-error" id="password-error" hidden>${msg("companyPasswordRequired")}</p>
      <input type="hidden" id="id-hidden-input" name="credentialId" <#if auth.selectedCredential?has_content>value="${auth.selectedCredential}"</#if>>
      <button class="company-button" name="login" id="kc-login" type="submit">${msg("companySignIn")}</button>
    </form>
    <script src="${url.resourcesPath}/js/company-sign-in.js"></script>
  </#if>
</@layout.registrationLayout>
