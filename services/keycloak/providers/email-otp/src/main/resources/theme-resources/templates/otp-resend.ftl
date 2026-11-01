<#--
  Shared resend control for both code pages.

  The cooldown is a server fact (`otpResendIn` seconds). Without a tick here the
  wait copy never changes and the submit never appears — the user would have to
  reload the page after a minute. When the clock hits zero, hide the wait and
  show the same POST the allowed-state already uses.
-->
<#macro form id>
        <form id="${id}" action="${url.loginAction}" method="post" class="${properties.kcFormClass!} pf-otp-resend">
            <input type="hidden" name="otpAction" value="resend"/>
            <#if otpResendAllowed>
                <button type="submit" class="pf-linkbutton">${msg("pfOtpResend")}</button>
            <#else>
                <span id="kc-otp-resend-wait" class="${properties.kcInputHelperTextClass!}"
                      data-seconds="${otpResendIn}">${msg("pfOtpResendWait", otpResendIn)}</span>
                <button type="submit" class="pf-linkbutton" hidden>${msg("pfOtpResend")}</button>
                <script>
                    (function () {
                        const wait = document.getElementById("kc-otp-resend-wait");
                        if (!wait) {
                            return;
                        }
                        const button = wait.parentElement.querySelector("button[type=submit]");
                        const label = wait.textContent;
                        let remaining = parseInt(wait.getAttribute("data-seconds"), 10);

                        const paint = function (seconds) {
                            wait.textContent = label.replace(/\d+/, String(seconds));
                        };
                        const ready = function () {
                            wait.hidden = true;
                            if (button) {
                                button.hidden = false;
                            }
                        };

                        if (!Number.isFinite(remaining) || remaining <= 0) {
                            ready();
                            return;
                        }

                        const tick = window.setInterval(function () {
                            remaining -= 1;
                            if (remaining <= 0) {
                                window.clearInterval(tick);
                                ready();
                                return;
                            }
                            paint(remaining);
                        }, 1000);
                    })();
                </script>
            </#if>
        </form>
</#macro>
