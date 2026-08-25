<#--
  Reached two ways, and the copy has to read correctly for both:
    - "Forgot password?" on the sign-in page.
    - The browser flow's `finish account setup` branch, whose Send Reset Email
      execution fires for an account that has neither passkey nor password —
      somebody who abandoned sign-up. For them nothing is being *re*set; they
      are setting a first password.
  Hence "set", not "reset" — the same reason the login theme words its shared
  page `updatePasswordTitle=Set your password`.
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
