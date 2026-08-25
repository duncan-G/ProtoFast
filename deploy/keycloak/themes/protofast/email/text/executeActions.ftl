<#ftl output_format="plainText">
<#assign requiredActionsText><#if requiredActions??><#list requiredActions><#items as reqActionItem>${msg("requiredAction.${reqActionItem}")}<#sep>, </#sep></#items></#list></#if></#assign>
${msg("pfActionsHeading")}

${msg("pfActionsLead", realmName, requiredActionsText)}

${link}

${msg("pfLinkExpires", linkExpirationFormatter(linkExpiration))}

${msg("pfActionsIgnore")}
