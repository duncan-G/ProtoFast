<#--
  Admin-triggered required actions ("Credential Reset" in the admin console).
  requiredActionsText is assembled in a plainText block exactly as base does it,
  so the action names arrive as a plain comma-separated list and the layout can
  escape it like any other copy.
-->
<#outputformat "plainText">
<#assign requiredActionsText><#if requiredActions??><#list requiredActions><#items as reqActionItem>${msg("requiredAction.${reqActionItem}")}<#sep>, </#sep></#items></#list></#if></#assign>
</#outputformat>

<#import "template.ftl" as layout>
<@layout.emailLayout heading=msg("pfActionsHeading") preheader=msg("pfActionsPreheader")>
  <@layout.pfText>${msg("pfActionsLead", realmName, requiredActionsText)}</@layout.pfText>
  <@layout.pfButton href=link label=msg("pfActionsAction")/>
  <@layout.pfNote>${msg("pfLinkExpires", linkExpirationFormatter(linkExpiration))}</@layout.pfNote>
  <@layout.pfNote>${msg("pfActionsIgnore")}</@layout.pfNote>
  <@layout.pfRule/>
  <@layout.pfFallback href=link/>
</@layout.emailLayout>
