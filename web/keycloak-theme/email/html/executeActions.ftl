<#ftl output_format="HTML">
<#-- The Axis email for an action link. Keycloak sends the invitation and an administrator's request to act through this
     one template, and the list of actions is all that tells them apart. The invitation asks for exactly two actions, a
     password and an authenticator. Any other set reads as a request that names its actions. -->
<#import "template.ftl" as layout>
<#assign actions = requiredActions![]>
<#assign invitation = actions?size == 2 && actions?seq_contains("UPDATE_PASSWORD") && actions?seq_contains("CONFIGURE_TOTP")>
<@layout.emailLayout; name>
<#if invitation>
<@layout.axisTitle>${msg("axisEmailInviteTitle", name)}</@layout.axisTitle>
<@layout.axisText>${msg("axisEmailInviteBody", name)}</@layout.axisText>
<@layout.axisButton href=link label=msg("axisEmailInviteButton") />
<#else>
<@layout.axisTitle>${msg("axisEmailActionsTitle")}</@layout.axisTitle>
<@layout.axisText>${msg("axisEmailActionsBody", name)}</@layout.axisText>
<ul style="margin:0 0 16px 0;padding-left:20px;">
<#list actions as action>
  <li>${msg("requiredAction." + action)}</li>
</#list>
</ul>
<@layout.axisButton href=link label=msg("axisEmailActionsButton") />
</#if>
<@layout.axisText>${msg("axisEmailLifetime", linkExpirationFormatter(linkExpiration))}</@layout.axisText>
</@layout.emailLayout>
