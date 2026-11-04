<#ftl output_format="plainText">
<#-- The plain-text part of the Axis email for an action link. It reads as the invitation or as a request exactly as the
     HTML part does, and it holds the link, which the HTML part leaves out. -->
<#assign realmId = (link!"")?keep_after("/realms/")?keep_before("/")>
<#assign name = (realmName?has_content && realmName != realmId)?then(realmName, "Axis")>
<#assign actions = requiredActions![]>
<#assign invitation = actions?size == 2 && actions?seq_contains("UPDATE_PASSWORD") && actions?seq_contains("CONFIGURE_TOTP")>
<#if invitation>
${msg("axisEmailInviteTitle", name)}

${msg("axisEmailInviteBody", name)}

${msg("axisEmailInviteButton")}: ${link}
<#else>
${msg("axisEmailActionsTitle")}

${msg("axisEmailActionsBody", name)}

<#list actions as action>
- ${msg("requiredAction." + action)}
</#list>

${msg("axisEmailActionsButton")}: ${link}
</#if>

${msg("axisEmailLifetime", linkExpirationFormatter(linkExpiration))}

${msg("axisEmailFooter")}
