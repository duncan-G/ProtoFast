<#--
  The LINK variant of sign-up verification, which this realm no longer sends:
  sign-up verifies by code (email-verification-with-code.ftl), because a link
  opened on a different device proves the address but leaves the session on the
  wrong machine. It stays overridden so that an administrator triggering the link
  form from the console still gets a message that looks like the product.

  The copy holds either way. An unverified address cannot be signed in to at all
  — with no passwords in the realm, the address IS the credential of last resort —
  so an account behind an unconfirmed one is inert, not a half-open door.
-->
<#import "template.ftl" as layout>
<@layout.emailLayout heading=msg("pfVerifyHeading") preheader=msg("pfVerifyPreheader")>
  <@layout.pfText>${msg("pfVerifyLead", realmName)}</@layout.pfText>
  <@layout.pfButton href=link label=msg("pfVerifyAction")/>
  <@layout.pfNote>${msg("pfLinkExpires", linkExpirationFormatter(linkExpiration))}</@layout.pfNote>
  <@layout.pfNote>${msg("pfVerifyIgnore", realmName)}</@layout.pfNote>
  <@layout.pfRule/>
  <@layout.pfFallback href=link/>
</@layout.emailLayout>
