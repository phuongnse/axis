<#-- Proposed: the email after "Send link": one button to set a new password, how long the link lasts, and that nothing
     changes when the person did not ask. -->
<#import "template.ftl" as layout>
<@layout.emailLayout>
<h1 style="margin:0 0 16px;font-size:20px;line-height:28px;font-weight:600;">${msg("axisPasswordResetTitle")}</h1>
<p style="margin:0;">${msg("axisPasswordResetBody", realmName!"Axis")}</p>
<@layout.button href=link label=msg("axisPasswordResetButton")/>
<p style="margin:0 0 8px;">${msg("axisLinkExpires", linkExpirationFormatter(linkExpiration))}</p>
<p style="margin:0;color:rgba(0,0,0,0.58);">${msg("axisPasswordResetIgnore")}</p>
</@layout.emailLayout>
