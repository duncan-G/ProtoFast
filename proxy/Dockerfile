FROM envoyproxy/envoy:v1.34-latest

RUN mkdir -p /etc/envoy/discovery
COPY entrypoint.sh envoy.yaml.tmpl envoy.rds.yaml.tmpl \
     cors-allow-origins-exact.tmpl cors-allow-origins-with-subdomain.tmpl /etc/envoy/
RUN chmod +x /etc/envoy/entrypoint.sh
