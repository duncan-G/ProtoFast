FROM envoyproxy/envoy:v1.34-latest

RUN mkdir -p /etc/envoy/discovery
COPY entrypoint.sh envoy.yaml.tmpl envoy.listener.yaml.tmpl \
     envoy.rds.yaml.tmpl envoy.vhost.yaml.tmpl envoy.cluster.yaml.tmpl /etc/envoy/
RUN chmod +x /etc/envoy/entrypoint.sh
