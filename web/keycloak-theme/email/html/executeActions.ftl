<#-- Proposed: the email that an invitation and an administrator's request send: what to do, as one button, how long
     the link lasts and what to do when it was not expected. -->
<#import "template.ftl" as layout>
<#outputformat "plainText">
<#assign steps><#if requiredActions??><#list requiredActions as action>${msg("requiredAction.${action}")}<#sep>, </#sep></#list></#if></#assign>
</#outputformat>
<@layout.emailLayout>
<h1 style="margin:0 0 16px;font-size:20px;line-height:28px;font-weight:600;">${msg("axisExecuteActionsTitle")}</h1>
<p style="margin:0 0 8px;">${msg("axisExecuteActionsBody", realmName!"Axis")}</p>
<p style="margin:0;font-weight:500;">${steps}</p>
<@layout.button href=link label=msg("axisExecuteActionsButton")/>
<p style="margin:0 0 8px;">${msg("axisLinkExpires", linkExpirationFormatter(linkExpiration))}</p>
<p style="margin:0;color:rgba(0,0,0,0.58);">${msg("axisExecuteActionsIgnore")}</p>
</@layout.emailLayout>
