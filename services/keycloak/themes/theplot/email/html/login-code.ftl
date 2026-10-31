<#--
  The sign-in code. The realm has no passwords, so for anyone without a passkey or
  a linked Google/Apple account this message *is* the way in — which is also why the
  copy is blunt about not sharing it and about what to do if it was not asked for.
  Nothing here is clickable: the code is typed back into the tab it was asked from.
-->
<#import "template.ftl" as layout>
<@layout.emailLayout heading=msg("pfSignInCodeHeading") preheader=msg("pfSignInCodePreheader")>
  <@layout.pfText>${msg("pfSignInCodeLead", realmName)}</@layout.pfText>
  <@layout.pfCode code=code/>
  <@layout.pfNote>${msg("pfSignInCodeExpires", codeLifetimeMinutes)}</@layout.pfNote>
  <@layout.pfNote>${msg("pfSignInCodeIgnore")}</@layout.pfNote>
</@layout.emailLayout>
