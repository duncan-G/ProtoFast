<#--
  The admin console's "Test connection" mail. Overridden for one reason beyond
  wording: it is the only email that can be sent on demand, so it is the way to
  eyeball this theme in a real client.
-->
<#import "template.ftl" as layout>
<@layout.emailLayout heading=msg("pfTestHeading") preheader=msg("pfTestPreheader")>
  <@layout.pfText>${msg("pfTestLead", realmName)}</@layout.pfText>
  <@layout.pfNote>${msg("pfTestNote")}</@layout.pfNote>
</@layout.emailLayout>
