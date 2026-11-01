#!/usr/bin/env bash
# ProtoFast's Keycloak entrypoint. It turns what ProtoStrap and the AppHost hand the
# container into Keycloak's own variables, then runs kc.sh with the container's arguments:
#
#   1. Each key file in /run/secrets (ProtoStrap's secret files) becomes the variable of its
#      name, such as KC_BOOTSTRAP_ADMIN_CLIENT_SECRET or ConnectionStrings__keycloak, unless
#      that variable is already set.
#   2. ConnectionStrings__keycloak, an ADO.NET-style connection string such as
#      Host=postgres;Port=5432;Username=keycloak;Password=...;Database=keycloak, sets
#      KC_DB_URL_HOST, KC_DB_URL_PORT, KC_DB_URL_DATABASE, KC_DB_USERNAME and
#      KC_DB_PASSWORD, each unless it is already set.
#   3. KC_HOSTNAME is ORIGIN_AUTH, the auth host's origin, unless it is already set.
#   4. Telemetry. The image has tracing and log export built in (they are build-time
#      options). With OTEL_EXPORTER_OTLP_ENDPOINT, both go there, with the protocol of
#      OTEL_EXPORTER_OTLP_PROTOCOL, the service name of OTEL_SERVICE_NAME, the resource
#      attributes of OTEL_RESOURCE_ATTRIBUTES and the headers of
#      OTEL_EXPORTER_OTLP_HEADERS; under ProtoStrap the console logger is turned off, so
#      each line leaves once. Without it, nothing is exported: log export is off and
#      traces Keycloak starts itself aren't sampled.
#   5. Under ProtoStrap (PROTOSTRAP_REPLICA), KC_CACHE_EMBEDDED_NETWORK_BIND_ADDRESS is the
#      replica's address on the overlay, unless it is already set. A release runs the new
#      replica beside the old one, often on another VM, and the default, SITE_LOCAL, may
#      take the docker_gwbridge address, which only its own VM reaches.
#
# "Already set" means set and not empty. It never prints a value. test/entrypoint_test.sh
# sources this file and calls its functions.

secrets_dir=/run/secrets
kc_sh=/opt/keycloak/bin/kc.sh

# set_default exports the variable named $1 as $2, unless the variable is already set or
# $2 is empty.
set_default() {
    local name=$1 value=$2
    [[ -n $value ]] || return 0
    [[ -n ${!name-} ]] && return 0
    export "$name=$value"
}

# load_secret_files exports each regular file in the directory $1 as the variable of its
# name, unless that variable is already set. ProtoStrap's agent links each key to
# ..data/<KEY>; the links are followed, while ..data and the other dot entries, and
# previous/, the values before a change, are skipped. A missing directory is no files.
load_secret_files() {
    local dir=$1 path name
    [[ -d $dir ]] || return 0
    for path in "$dir"/*; do
        name=${path##*/}
        [[ $name == previous ]] && continue
        [[ -f $path ]] || continue
        if [[ ! $name =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]]; then
            echo "entrypoint: skipping secret file $name: not a variable name" >&2
            continue
        fi
        set_default "$name" "$(<"$path")"
    done
}

# parse_connection_string reads an ADO.NET-style connection string, $1, into the global
# associative array connection, with each key lower-cased and without spaces ("User ID" is
# userid). Keys and unquoted values are trimmed. A value may be quoted with " or ', in
# which the quote doubled is the quote itself, so it can hold ; and =. It returns 1, never
# printing the string, for an unterminated quote or text after a closing quote.
parse_connection_string() {
    local text=$1 length=${#1} i c key='' value='' in_value=0 quote='' quoted=0 closed=0
    declare -gA connection=()
    for ((i = 0; i <= length; i++)); do
        c=${text:i:1}
        if [[ -n $quote ]]; then
            if ((i == length)); then
                return 1
            elif [[ $c == "$quote" && ${text:i+1:1} == "$quote" ]]; then
                value+=$c
                ((i++))
            elif [[ $c == "$quote" ]]; then
                quote='' closed=1
            else
                value+=$c
            fi
            continue
        fi
        if ((i == length)) || [[ $c == ';' ]]; then
            key=${key,,}
            key=${key//[[:space:]]/}
            if ((!quoted)); then
                value=${value#"${value%%[![:space:]]*}"}
                value=${value%"${value##*[![:space:]]}"}
            fi
            [[ -n $key ]] && connection[$key]=$value
            key='' value='' in_value=0 quoted=0 closed=0
            continue
        fi
        if ((!in_value)); then
            if [[ $c == '=' ]]; then
                in_value=1
            else
                key+=$c
            fi
        elif ((closed)); then
            [[ $c == [[:space:]] ]] || return 1
        elif [[ ($c == '"' || $c == "'") && -z ${value//[[:space:]]/} ]]; then
            quote=$c quoted=1 value=''
        else
            value+=$c
        fi
    done
    return 0
}

# apply_connection_string sets Keycloak's database variables from ConnectionStrings__keycloak,
# if it is set. Host's alias is Server; Username's are User ID, User Id, UserId, User Name,
# User and UID; Password's are PWD and PSW; Database's is DB. Other keys are ignored.
apply_connection_string() {
    [[ -n ${ConnectionStrings__keycloak-} ]] || return 0
    if ! parse_connection_string "$ConnectionStrings__keycloak"; then
        echo "entrypoint: ConnectionStrings__keycloak isn't a valid connection string" >&2
        return 1
    fi
    local key host='' port='' username='' password='' database=''
    for key in "${!connection[@]}"; do
        case $key in
            host | server) host=${connection[$key]} ;;
            port) port=${connection[$key]} ;;
            username | userid | user | uid) username=${connection[$key]} ;;
            password | pwd | psw) password=${connection[$key]} ;;
            database | db) database=${connection[$key]} ;;
        esac
    done
    set_default KC_DB_URL_HOST "$host"
    set_default KC_DB_URL_PORT "$port"
    set_default KC_DB_URL_DATABASE "$database"
    set_default KC_DB_USERNAME "$username"
    set_default KC_DB_PASSWORD "$password"
}

# otlp_protocol prints Keycloak's name for OTEL_EXPORTER_OTLP_PROTOCOL. Keycloak exports
# grpc and http/protobuf only; an unset protocol is OpenTelemetry's default, http/protobuf,
# and anything else, such as http/json, is sent as http/protobuf, which the same port takes.
otlp_protocol() {
    local protocol=${OTEL_EXPORTER_OTLP_PROTOCOL-}
    case $protocol in
        grpc) echo grpc ;;
        '' | http/protobuf) echo http/protobuf ;;
        *)
            echo "entrypoint: Keycloak can't export with OTEL_EXPORTER_OTLP_PROTOCOL $protocol; using http/protobuf" >&2
            echo http/protobuf
            ;;
    esac
}

# percent_decode prints $1 with each %XX, XX two hex digits, as the byte it stands for.
percent_decode() {
    local text=$1 out='' byte
    while [[ $text =~ ^([^%]*)%([0-9A-Fa-f]{2})(.*)$ ]]; do
        # shellcheck disable=SC2059 # The format is the escape \xXX itself.
        printf -v byte "\\x${BASH_REMATCH[2]}"
        out+=${BASH_REMATCH[1]}$byte
        text=${BASH_REMATCH[3]}
    done
    printf '%s' "$out$text"
}

# apply_otlp_headers sets KC_TELEMETRY_HEADER_<NAME> for each name=value in
# OTEL_EXPORTER_OTLP_HEADERS, such as the Aspire dashboard's x-otlp-api-key: Keycloak sends
# KC_TELEMETRY_HEADER_X_OTLP_API_KEY as the header x-otlp-api-key with every export. Values
# are percent-decoded, as OpenTelemetry's SDKs decode them. A name Keycloak's variables
# can't spell (anything but letters, digits and single dashes) is skipped, naming only the
# header.
apply_otlp_headers() {
    local headers=${OTEL_EXPORTER_OTLP_HEADERS-} pair name value variable
    [[ -n $headers ]] || return 0
    local -a pairs
    IFS=',' read -r -a pairs <<<"$headers"
    for pair in "${pairs[@]}"; do
        [[ $pair == *=* ]] || continue
        name=${pair%%=*}
        value=${pair#*=}
        name=${name#"${name%%[![:space:]]*}"}
        name=${name%"${name##*[![:space:]]}"}
        value=${value#"${value%%[![:space:]]*}"}
        value=${value%"${value##*[![:space:]]}"}
        [[ -n $name ]] || continue
        if [[ ! $name =~ ^[A-Za-z0-9]+(-[A-Za-z0-9]+)*$ ]]; then
            echo "entrypoint: skipping OTLP header $name: Keycloak can't take its name" >&2
            continue
        fi
        variable=${name//-/_}
        variable=KC_TELEMETRY_HEADER_${variable^^}
        set_default "$variable" "$(percent_decode "$value")"
    done
}

# apply_telemetry points Keycloak's exporters at OTEL_EXPORTER_OTLP_ENDPOINT, or keeps
# them quiet without one. For http/protobuf Keycloak adds /v1/traces and /v1/logs to the
# endpoint itself, so it gets the endpoint as OpenTelemetry's variable gives it.
apply_telemetry() {
    local endpoint=${OTEL_EXPORTER_OTLP_ENDPOINT-} protocol
    if [[ -z $endpoint ]]; then
        set_default KC_TELEMETRY_LOGS_LEVEL off
        set_default KC_TRACING_SAMPLER_RATIO 0.0
        return 0
    fi
    endpoint=${endpoint%/}
    protocol=$(otlp_protocol)
    set_default KC_TRACING_ENDPOINT "$endpoint"
    set_default KC_TRACING_PROTOCOL "$protocol"
    set_default KC_TELEMETRY_LOGS_ENDPOINT "$endpoint"
    set_default KC_TELEMETRY_LOGS_PROTOCOL "$protocol"
    set_default KC_TELEMETRY_SERVICE_NAME "${OTEL_SERVICE_NAME-}"
    set_default KC_TELEMETRY_RESOURCE_ATTRIBUTES "${OTEL_RESOURCE_ATTRIBUTES-}"
    apply_otlp_headers
    # Under ProtoStrap the logs leave by OTLP alone; stdout would send each line again.
    if [[ -n ${PROTOSTRAP_REPLICA-} && ${KC_TELEMETRY_LOGS_LEVEL-} != off ]]; then
        set_default KC_LOG_CONSOLE_LEVEL off
    fi
}

# apply_cluster_address binds the cluster to the address the replica's name, its container's,
# resolves to on the overlay. Unresolved, it leaves Keycloak's default.
apply_cluster_address() {
    [[ -n ${PROTOSTRAP_REPLICA-} ]] || return 0
    local address=''
    read -r address _ < <(getent hosts "$PROTOSTRAP_REPLICA") || true
    if [[ -z $address ]]; then
        echo "entrypoint: $PROTOSTRAP_REPLICA has no address on the overlay; the cluster binds by Keycloak's default" >&2
        return 0
    fi
    set_default KC_CACHE_EMBEDDED_NETWORK_BIND_ADDRESS "$address"
}

main() {
    load_secret_files "$secrets_dir"
    apply_connection_string || exit 1
    set_default KC_HOSTNAME "${ORIGIN_AUTH-}"
    apply_telemetry
    apply_cluster_address
    exec "$kc_sh" "$@"
}

if [[ ${BASH_SOURCE[0]} == "$0" ]]; then
    set -euo pipefail
    main "$@"
fi
