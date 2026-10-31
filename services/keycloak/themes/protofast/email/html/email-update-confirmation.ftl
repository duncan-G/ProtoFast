<#--
  Confirming a NEW address the user typed into the account console. It lands in
  the new inbox, so it names the address explicitly — the recipient may have no
  other context for why Protofast is writing to them.
-->
<#import "template.ftl" as layout>
<@layout.emailLayout heading=msg("pfEmailUpdateHeading") preheader=msg("pfEmailUpdatePreheader")>
  <@layout.pfText>${msg("pfEmailUpdateLead", realmName, newEmail)}</@layout.pfText>
  <@layout.pfButton href=link label=msg("pfEmailUpdateAction")/>
  <@layout.pfNote>${msg("pfLinkExpires", linkExpirationFormatter(linkExpiration))}</@layout.pfNote>
  <@layout.pfNote>${msg("pfEmailUpdateIgnore")}</@layout.pfNote>
  <@layout.pfRule/>
  <@layout.pfFallback href=link/>
</@layout.emailLayout>
