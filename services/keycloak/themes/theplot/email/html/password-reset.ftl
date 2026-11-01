<#--
  Unreachable in this realm and kept only so the inherited template still resolves.
  There are no passwords to reset: resetPasswordAllowed is off, the reset-credentials
  flow is unbound, and the sign-in page has no "Forgot password?" link. An account
  with no passkey signs in with a code mailed to its address instead.

  If this ever arrives in somebody's inbox, something is misconfigured. The wording
  stays "set" rather than "reset" so that it would at least read correctly.
-->
<#import "template.ftl" as layout>
<@layout.emailLayout heading=msg("pfResetHeading") preheader=msg("pfResetPreheader")>
  <@layout.pfText>${msg("pfResetLead", realmName)}</@layout.pfText>
  <@layout.pfButton href=link label=msg("pfResetAction")/>
  <@layout.pfNote>${msg("pfLinkExpires", linkExpirationFormatter(linkExpiration))}</@layout.pfNote>
  <@layout.pfNote>${msg("pfResetIgnore")}</@layout.pfNote>
  <@layout.pfRule/>
  <@layout.pfFallback href=link/>
</@layout.emailLayout>
