<#-- The sample company's sign-in form: username or email, password and one button, with Keycloak's error under the
     fields. -->
<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('username','password'); section>
  <#if section = "header">
    ${msg("companyWelcome")}
  <#elseif section = "form">
    <form class="company-form" action="${url.loginAction}" method="post" onsubmit="login.disabled = true; return true;">
      <label class="company-label" for="username">${msg("usernameOrEmail")}</label>
      <input class="company-input" id="username" name="username" type="text" value="${(login.username!'')}" autofocus
             autocomplete="username" aria-invalid="<#if messagesPerField.existsError('username','password')>true</#if>">
      <label class="company-label" for="password">${msg("password")}</label>
      <input class="company-input" id="password" name="password" type="password" autocomplete="current-password"
             aria-invalid="<#if messagesPerField.existsError('username','password')>true</#if>">
      <#if messagesPerField.existsError('username','password')>
        <p class="company-error" aria-live="polite">${kcSanitize(messagesPerField.getFirstError('username','password'))?no_esc}</p>
      </#if>
      <input type="hidden" id="id-hidden-input" name="credentialId" <#if auth.selectedCredential?has_content>value="${auth.selectedCredential}"</#if>>
      <button class="company-button" name="login" id="kc-login" type="submit">${msg("companySignIn")}</button>
    </form>
  </#if>
</@layout.registrationLayout>
