<#ftl output_format="plainText">
<#assign steps><#if requiredActions??><#list requiredActions as action>${msg("requiredAction.${action}")}<#sep>, </#sep></#list></#if></#assign>
${msg("axisExecuteActionsTitle")}

${msg("axisExecuteActionsBody", realmName!"Axis")}
${steps}

${link}

${msg("axisLinkExpires", linkExpirationFormatter(linkExpiration))}
${msg("axisExecuteActionsIgnore")}
