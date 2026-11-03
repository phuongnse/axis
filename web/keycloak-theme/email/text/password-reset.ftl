<#ftl output_format="plainText">
<#-- The plain-text part of the Axis password reset email. It holds the link, which the HTML part leaves out. -->
<#assign realmId = (link!"")?keep_after("/realms/")?keep_before("/")>
<#assign name = (realmName?has_content && realmName != realmId)?then(realmName, "Axis")>
${msg("axisEmailResetTitle")}

${msg("axisEmailResetBody", name)}

${msg("axisEmailResetButton")}: ${link}

${msg("axisEmailLifetime", linkExpirationFormatter(linkExpiration))}

${msg("axisEmailFooter")}
