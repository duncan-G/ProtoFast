<#--
  The code-entry variant of verification (no link — the user types the code back
  into the page they came from). Nothing here is clickable, so there is no button
  and no fallback URL.
-->
<#import "template.ftl" as layout>
<@layout.emailLayout heading=msg("pfVerifyHeading") preheader=msg("pfVerifyCodePreheader")>
  <@layout.pfText>${msg("pfVerifyCodeLead", realmName)}</@layout.pfText>
  <@layout.pfCode code=code/>
  <@layout.pfNote>${msg("pfVerifyCodeIgnore")}</@layout.pfNote>
</@layout.emailLayout>
