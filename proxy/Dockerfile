FROM envoyproxy/envoy:v1.34-latest

RUN mkdir -p /etc/envoy/discovery
COPY entrypoint.sh envoy.yaml.tmpl envoy.listener.yaml.tmpl \
     envoy.rds.yaml.tmpl envoy.vhost.yaml.tmpl envoy.cluster.yaml.tmpl /etc/envoy/
COPY errors/ /etc/envoy/errors/
RUN chmod +x /etc/envoy/entrypoint.sh

# Bake a long-lived self-signed cert for the internal hop. In publish mode the
# only thing in front of Envoy is cloudflared on the same Docker network, which
# dials https://envoy:8443 with originRequest.noTLSVerify=true — so this cert is
# never validated and never faces the public internet (public TLS is terminated
# at the Cloudflare edge). See deployment-plan §2.2 (Option 1).
#
# entrypoint.sh requires ENVOY_TLS_CERT/ENVOY_TLS_KEY unconditionally; defaulting
# them here lets publish mode start with no externally supplied cert. Both remain
# overridable, so dev (which injects the Aspire dev cert) is unchanged.
RUN openssl req -x509 -newkey rsa:2048 -days 3650 -nodes \
      -keyout /etc/envoy/internal-tls.key \
      -out /etc/envoy/internal-tls.crt \
      -subj "/CN=envoy" \
    && chmod 644 /etc/envoy/internal-tls.crt /etc/envoy/internal-tls.key

ENV ENVOY_TLS_CERT=/etc/envoy/internal-tls.crt \
    ENVOY_TLS_KEY=/etc/envoy/internal-tls.key

# The base image's default CMD boots Envoy directly against the sample
# /etc/envoy/envoy.yaml. Run our renderer instead: entrypoint.sh generates
# /tmp/envoy.yaml + /etc/envoy/discovery/*.yaml, then exec's Envoy. Setting
# ENTRYPOINT also resets the inherited CMD to empty, so no args reach the script.
ENTRYPOINT ["/etc/envoy/entrypoint.sh"]
