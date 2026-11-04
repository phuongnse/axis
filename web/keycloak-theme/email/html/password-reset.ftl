<#ftl output_format="HTML">
<#-- The Axis password reset email. It has one button and the link's lifetime. The link itself is only in the plain-text
     part, because an action link holds a token of some 2,000 characters. -->
<#import "template.ftl" as layout>
<@layout.emailLayout; name>
<@layout.axisTitle>${msg("axisEmailResetTitle")}</@layout.axisTitle>
<@layout.axisText>${msg("axisEmailResetBody", name)}</@layout.axisText>
<@layout.axisButton href=link label=msg("axisEmailResetButton") />
<@layout.axisText>${msg("axisEmailLifetime", linkExpirationFormatter(linkExpiration))}</@layout.axisText>
</@layout.emailLayout>
