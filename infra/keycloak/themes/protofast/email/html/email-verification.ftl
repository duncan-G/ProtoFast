<#--
  Sign-up verification. The realm sets verifyEmail=true with VERIFY_EMAIL as a
  default required action, so this is the first thing a new account ever
  receives — and, because registration-password-action defers the password until
  after verification, the account behind it has no credential yet. The copy says
  so: an unverified account is inert, not a half-open door.
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
