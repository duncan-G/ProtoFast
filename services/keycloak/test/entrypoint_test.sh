#!/usr/bin/env bash
# Tests for ../entrypoint.sh. Each runs the entrypoint's main in a clean environment
# (env -i), with its secrets directory in a temporary directory and kc.sh replaced by a
# stub that records its arguments and exported variables.
#
#   bash test/entrypoint_test.sh                 # from services/keycloak, bash 4.4 or later
#
# or in a container, since Docker Desktop can't mount every directory:
#
#   tar c entrypoint.sh test | docker run --rm -i bash:5 \
#     bash -c 'mkdir /k && cd /k && tar x && bash test/entrypoint_test.sh'
#
# It prints one line per failed check and a summary, and exits 1 if any failed.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
entrypoint=$here/../entrypoint.sh
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

checks=0
failures=0
current=''

# The stub kc.sh writes its arguments, one per line, and `export -p` to files.
cat >"$work/kc.sh" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$@" >"$STUB_DIR/args"
export -p >"$STUB_DIR/env"
STUB
chmod +x "$work/kc.sh"

# The stub getent resolves keycloak-1, a replica's container name, on the overlay.
mkdir -p "$work/bin"
cat >"$work/bin/getent" <<'STUB'
#!/usr/bin/env bash
[[ $1 == hosts && $2 == keycloak-1 ]] || exit 2
echo "10.200.0.7      keycloak-1"
STUB
chmod +x "$work/bin/getent"

# scenario starts a test: a fresh secrets directory and stub output.
scenario() {
    current=$1
    rm -rf "$work/secrets" "$work/out"
    mkdir -p "$work/out"
    secrets=$work/secrets
}

# run runs the entrypoint's main with only the given NAME=value variables, and the
# container's arguments after --. It keeps its exit status in status and what it printed
# in output.
run() {
    local vars=()
    while (($#)) && [[ $1 != -- ]]; do
        vars+=("$1")
        shift
    done
    (($#)) && shift
    # shellcheck disable=SC2016 # The inner script expands its own arguments.
    output=$(env -i PATH="$work/bin:$PATH" STUB_DIR="$work/out" "${vars[@]}" bash -c '
        source "$1"
        secrets_dir=$2
        kc_sh=$3
        shift 3
        set -euo pipefail
        main "$@"' entrypoint "$entrypoint" "$secrets" "$work/kc.sh" "$@" 2>&1)
    status=$?
}

# got prints the variable $1 as kc.sh saw it, or <unset>.
got() {
    [[ -f $work/out/env ]] || {
        printf '<kc.sh not run>'
        return
    }
    (
        # shellcheck disable=SC1091
        source "$work/out/env" 2>/dev/null
        if [[ -v $1 ]]; then printf '%s' "${!1}"; else printf '<unset>'; fi
    )
}

check() {
    local what=$1 expected=$2 actual=$3
    ((checks++))
    if [[ $expected != "$actual" ]]; then
        ((failures++))
        printf 'FAIL %s: %s\n  expected: [%s]\n  got:      [%s]\n' "$current" "$what" "$expected" "$actual"
    fi
}

check_var() {
    check "$1" "$2" "$(got "$1")"
}

# secret writes a key file as the agent does: no trailing newline.
secret() {
    mkdir -p "$secrets"
    printf '%s' "$2" >"$secrets/$1"
}

# --- kc.sh ---------------------------------------------------------------------------

scenario "it execs kc.sh with the container's arguments"
run -- start --optimized --log-level=info
check status 0 "$status"
check args $'start\n--optimized\n--log-level=info' "$(cat "$work/out/args")"
check output '' "$output"

scenario "it execs kc.sh with no arguments"
run --
check status 0 "$status"
check args '' "$(cat "$work/out/args")"

# --- secret files ----------------------------------------------------------------------

scenario "each secret file becomes the variable of its name"
secret KC_BOOTSTRAP_ADMIN_CLIENT_SECRET 's3cr3t value'
secret Oidc__ClientSecret 'client-secret'
run -- start
check status 0 "$status"
check_var KC_BOOTSTRAP_ADMIN_CLIENT_SECRET 's3cr3t value'
check_var Oidc__ClientSecret 'client-secret'
check output '' "$output"

scenario "a variable that is already set wins over its file"
secret KC_BOOTSTRAP_ADMIN_CLIENT_SECRET 'from-file'
run KC_BOOTSTRAP_ADMIN_CLIENT_SECRET=from-env -- start
check_var KC_BOOTSTRAP_ADMIN_CLIENT_SECRET 'from-env'

scenario "an empty variable counts as unset"
secret KC_BOOTSTRAP_ADMIN_CLIENT_SECRET 'from-file'
run KC_BOOTSTRAP_ADMIN_CLIENT_SECRET= -- start
check_var KC_BOOTSTRAP_ADMIN_CLIENT_SECRET 'from-file'

scenario "a value is taken as it is, never evaluated"
value=$'a "quoted" $HOME `id` $(id) \\ ; & | * ?\nsecond line'
secret KC_BOOTSTRAP_ADMIN_CLIENT_SECRET "$value"
run -- start
check status 0 "$status"
check_var KC_BOOTSTRAP_ADMIN_CLIENT_SECRET "$value"

scenario "the agent's layout: links to ..data, and previous/ skipped"
mkdir -p "$secrets/..v-2" "$secrets/..v-1"
printf '%s' 'new-secret' >"$secrets/..v-2/KC_BOOTSTRAP_ADMIN_CLIENT_SECRET"
printf '%s' 'old-secret' >"$secrets/..v-1/KC_BOOTSTRAP_ADMIN_CLIENT_SECRET"
ln -s ..v-2 "$secrets/..data"
ln -s ..v-1 "$secrets/previous"
ln -s ..data/KC_BOOTSTRAP_ADMIN_CLIENT_SECRET "$secrets/KC_BOOTSTRAP_ADMIN_CLIENT_SECRET"
run -- start
check status 0 "$status"
check_var KC_BOOTSTRAP_ADMIN_CLIENT_SECRET 'new-secret'
check_var previous '<unset>'
check output '' "$output"

scenario "a previous/ directory that isn't a link is skipped too"
secret KC_BOOTSTRAP_ADMIN_CLIENT_SECRET 'current'
mkdir -p "$secrets/previous"
printf '%s' 'old' >"$secrets/previous/KC_BOOTSTRAP_ADMIN_CLIENT_SECRET"
run -- start
check_var KC_BOOTSTRAP_ADMIN_CLIENT_SECRET 'current'

scenario "a directory is skipped"
secret KC_DB_PASSWORD 'pw'
mkdir -p "$secrets/SOME_DIRECTORY"
printf '%s' 'inside' >"$secrets/SOME_DIRECTORY/KEY"
run -- start
check status 0 "$status"
check_var SOME_DIRECTORY '<unset>'
check_var KEY '<unset>'
check_var KC_DB_PASSWORD 'pw'
check output '' "$output"

scenario "a file whose name isn't a variable name is skipped, naming only the file"
secret 'tls.crt' 'certificate-body'
secret KC_DB_PASSWORD 'pw'
run -- start
check status 0 "$status"
check_var KC_DB_PASSWORD 'pw'
check output 'entrypoint: skipping secret file tls.crt: not a variable name' "$output"

scenario "without /run/secrets there are no files"
run -- start
check status 0 "$status"
check_var KC_BOOTSTRAP_ADMIN_CLIENT_SECRET '<unset>'

scenario "an empty secrets directory is no files"
mkdir -p "$secrets"
run -- start
check status 0 "$status"

# --- the connection string ----------------------------------------------------------

scenario "the connection string sets Keycloak's database variables"
run 'ConnectionStrings__keycloak=Host=postgres;Port=5432;Username=keycloak;Password=pa55;Database=keycloak' -- start
check status 0 "$status"
check_var KC_DB_URL_HOST postgres
check_var KC_DB_URL_PORT 5432
check_var KC_DB_URL_DATABASE keycloak
check_var KC_DB_USERNAME keycloak
check_var KC_DB_PASSWORD pa55
check output '' "$output"

scenario "the connection string can come from a secret file"
secret ConnectionStrings__keycloak 'Host=postgres;Port=5432;Username=keycloak;Password=from-file;Database=keycloak'
run -- start
check_var KC_DB_PASSWORD from-file
check_var KC_DB_URL_HOST postgres

scenario "keys are case-insensitive"
run 'ConnectionStrings__keycloak=HOST=db;PORT=6432;USERNAME=u;PassWord=p;DataBase=d' -- start
check_var KC_DB_URL_HOST db
check_var KC_DB_URL_PORT 6432
check_var KC_DB_USERNAME u
check_var KC_DB_PASSWORD p
check_var KC_DB_URL_DATABASE d

for alias in 'Server' 'server' 'SERVER'; do
    scenario "$alias is Host"
    run "ConnectionStrings__keycloak=$alias=db.internal;Username=u" -- start
    check_var KC_DB_URL_HOST db.internal
done

for alias in 'User ID' 'User Id' 'user id' 'UserId' 'UserName' 'User Name' 'User' 'UID'; do
    scenario "$alias is Username"
    run "ConnectionStrings__keycloak=Host=h;$alias=keycloak_user;Password=p" -- start
    check_var KC_DB_USERNAME keycloak_user
done

for alias in 'PWD' 'Psw'; do
    scenario "$alias is Password"
    run "ConnectionStrings__keycloak=Host=h;$alias=pw" -- start
    check_var KC_DB_PASSWORD pw
done

scenario "DB is Database"
run 'ConnectionStrings__keycloak=Host=h;DB=kc' -- start
check_var KC_DB_URL_DATABASE kc

scenario "keys and unquoted values are trimmed"
run 'ConnectionStrings__keycloak=  Host = postgres ; Port= 5432 ;User Id =  keycloak  ; Password = p w ' -- start
check_var KC_DB_URL_HOST postgres
check_var KC_DB_URL_PORT 5432
check_var KC_DB_USERNAME keycloak
check_var KC_DB_PASSWORD 'p w'

scenario "an unquoted value may hold ="
run 'ConnectionStrings__keycloak=Host=h;Password=YWJj==;Database=d' -- start
check_var KC_DB_PASSWORD 'YWJj=='
check_var KC_DB_URL_DATABASE d

scenario "a double-quoted value may hold ; = and a doubled quote"
run 'ConnectionStrings__keycloak=Host=h;Password="a;b=c ""d"" ";Database=d' -- start
check status 0 "$status"
check_var KC_DB_PASSWORD 'a;b=c "d" '
check_var KC_DB_URL_DATABASE d

scenario "a single-quoted value may hold ; and a doubled quote"
run "ConnectionStrings__keycloak=Host=h;Password= 'it''s;ok' ;Database=d" -- start
check status 0 "$status"
check_var KC_DB_PASSWORD "it's;ok"
check_var KC_DB_URL_DATABASE d

scenario "a quote inside an unquoted value is just a character"
run "ConnectionStrings__keycloak=Host=h;Password=a'b\"c" -- start
check_var KC_DB_PASSWORD "a'b\"c"

scenario "empty segments and a trailing semicolon are ignored"
run 'ConnectionStrings__keycloak=;Host=h;;Port=5432;' -- start
check status 0 "$status"
check_var KC_DB_URL_HOST h
check_var KC_DB_URL_PORT 5432

scenario "other keys are ignored"
run 'ConnectionStrings__keycloak=Host=h;SSL Mode=Require;Pooling=true;Timeout=15;Database=d' -- start
check status 0 "$status"
check_var KC_DB_URL_HOST h
check_var KC_DB_URL_DATABASE d

scenario "a key the string leaves out stays unset"
run 'ConnectionStrings__keycloak=Host=h;Username=u;Password=p' -- start
check_var KC_DB_URL_PORT '<unset>'
check_var KC_DB_URL_DATABASE '<unset>'

scenario "a variable that is already set wins over the connection string"
run 'ConnectionStrings__keycloak=Host=h;Port=5432;Username=u;Password=p;Database=d' \
    KC_DB_PASSWORD=explicit KC_DB_URL_HOST=other-host -- start
check_var KC_DB_PASSWORD explicit
check_var KC_DB_URL_HOST other-host
check_var KC_DB_USERNAME u

scenario "an empty database variable counts as unset"
run 'ConnectionStrings__keycloak=Host=h;Password=p' KC_DB_PASSWORD= -- start
check_var KC_DB_PASSWORD p

scenario "without a connection string the database variables are untouched"
run KC_DB_URL_HOST=given -- start
check_var KC_DB_URL_HOST given
check_var KC_DB_PASSWORD '<unset>'

scenario "an unterminated quote fails without running kc.sh or printing the string"
run 'ConnectionStrings__keycloak=Host=h;Password="unterminated-secret' -- start
check status 1 "$status"
check 'kc.sh ran' 'no' "$([[ -f $work/out/args ]] && echo yes || echo no)"
check output "entrypoint: ConnectionStrings__keycloak isn't a valid connection string" "$output"

scenario "text after a closing quote fails"
run 'ConnectionStrings__keycloak=Host=h;Password="quoted"trailing-secret' -- start
check status 1 "$status"
check output "entrypoint: ConnectionStrings__keycloak isn't a valid connection string" "$output"

# --- the hostname ---------------------------------------------------------------------

scenario "KC_HOSTNAME is ORIGIN_AUTH"
run ORIGIN_AUTH=http://auth.localhost:20000 -- start-dev
check_var KC_HOSTNAME http://auth.localhost:20000
check args start-dev "$(cat "$work/out/args")"

scenario "KC_HOSTNAME that is set wins over ORIGIN_AUTH"
run ORIGIN_AUTH=https://auth.protofast.dev KC_HOSTNAME=https://sso.example.com -- start
check_var KC_HOSTNAME https://sso.example.com

scenario "without ORIGIN_AUTH KC_HOSTNAME stays unset"
run -- start
check_var KC_HOSTNAME '<unset>'

# --- telemetry ---------------------------------------------------------------------

scenario "without an OTLP endpoint nothing is exported"
run -- start --optimized
check status 0 "$status"
check_var KC_TELEMETRY_LOGS_LEVEL off
check_var KC_TRACING_SAMPLER_RATIO 0.0
check_var KC_TRACING_ENDPOINT '<unset>'
check_var KC_TELEMETRY_LOGS_ENDPOINT '<unset>'
check_var KC_LOG_CONSOLE_LEVEL '<unset>'
check output '' "$output"

scenario "without an OTLP endpoint, a sampler ratio or log level that is set is kept"
run KC_TRACING_SAMPLER_RATIO=0.25 KC_TELEMETRY_LOGS_LEVEL=warn -- start
check_var KC_TRACING_SAMPLER_RATIO 0.25
check_var KC_TELEMETRY_LOGS_LEVEL warn

scenario "ProtoStrap's collector gets traces and logs over http/protobuf"
run OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318 OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf \
    OTEL_SERVICE_NAME=keycloak \
    'OTEL_RESOURCE_ATTRIBUTES=service.instance.id=keycloak-1,service.version=r7,deployment.environment.name=prod' -- start --optimized
check status 0 "$status"
check_var KC_TRACING_ENDPOINT http://telemetry:4318
check_var KC_TRACING_PROTOCOL http/protobuf
check_var KC_TELEMETRY_LOGS_ENDPOINT http://telemetry:4318
check_var KC_TELEMETRY_LOGS_PROTOCOL http/protobuf
check_var KC_TELEMETRY_SERVICE_NAME keycloak
check_var KC_TELEMETRY_RESOURCE_ATTRIBUTES 'service.instance.id=keycloak-1,service.version=r7,deployment.environment.name=prod'
check_var KC_TELEMETRY_LOGS_LEVEL '<unset>'
check_var KC_TRACING_SAMPLER_RATIO 0.0
check output '' "$output"

scenario "the endpoint's trailing slash is dropped: Keycloak adds /v1/traces and /v1/logs"
run OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318/ -- start
check_var KC_TRACING_ENDPOINT http://telemetry:4318
check_var KC_TELEMETRY_LOGS_ENDPOINT http://telemetry:4318

scenario "an endpoint with a path keeps it"
run OTEL_EXPORTER_OTLP_ENDPOINT=https://collector.example.com/otlp -- start
check_var KC_TRACING_ENDPOINT https://collector.example.com/otlp

scenario "grpc is grpc"
run OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 OTEL_EXPORTER_OTLP_PROTOCOL=grpc -- start-dev
check_var KC_TRACING_PROTOCOL grpc
check_var KC_TELEMETRY_LOGS_PROTOCOL grpc
check output '' "$output"

scenario "an unset protocol is OpenTelemetry's default, http/protobuf, not Keycloak's grpc"
run OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318 -- start
check_var KC_TRACING_PROTOCOL http/protobuf
check_var KC_TELEMETRY_LOGS_PROTOCOL http/protobuf
check output '' "$output"

scenario "http/json, which Keycloak can't send, is sent as http/protobuf, saying so"
run OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318 OTEL_EXPORTER_OTLP_PROTOCOL=http/json -- start
check status 0 "$status"
check_var KC_TRACING_PROTOCOL http/protobuf
check output "entrypoint: Keycloak can't export with OTEL_EXPORTER_OTLP_PROTOCOL http/json; using http/protobuf" "$output"

scenario "Keycloak variables that are set win over OpenTelemetry's"
run OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318 OTEL_SERVICE_NAME=keycloak \
    KC_TRACING_ENDPOINT=http://tempo:4318 KC_TELEMETRY_LOGS_PROTOCOL=grpc KC_TELEMETRY_SERVICE_NAME=idp -- start
check_var KC_TRACING_ENDPOINT http://tempo:4318
check_var KC_TELEMETRY_LOGS_ENDPOINT http://telemetry:4318
check_var KC_TELEMETRY_LOGS_PROTOCOL grpc
check_var KC_TRACING_PROTOCOL http/protobuf
check_var KC_TELEMETRY_SERVICE_NAME idp

scenario "without OTEL_SERVICE_NAME or OTEL_RESOURCE_ATTRIBUTES Keycloak keeps its own"
run OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318 -- start
check_var KC_TELEMETRY_SERVICE_NAME '<unset>'
check_var KC_TELEMETRY_RESOURCE_ATTRIBUTES '<unset>'

scenario "the Aspire dashboard's API key header is sent with every export, never printed"
run OTEL_EXPORTER_OTLP_ENDPOINT=http://host.docker.internal:21000 OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf \
    'OTEL_EXPORTER_OTLP_HEADERS=x-otlp-api-key=0123456789abcdef' -- start-dev
check status 0 "$status"
check_var KC_TELEMETRY_HEADER_X_OTLP_API_KEY 0123456789abcdef
check output '' "$output"

scenario "several headers, spaces around them, and percent-encoded values"
run OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318 \
    'OTEL_EXPORTER_OTLP_HEADERS= Authorization = Basic%20dXNlcjpwYXNz , tenant=a%2Cb%25c,x-empty=' -- start
check_var KC_TELEMETRY_HEADER_AUTHORIZATION 'Basic dXNlcjpwYXNz'
check_var KC_TELEMETRY_HEADER_TENANT 'a,b%c'
check_var KC_TELEMETRY_HEADER_X_EMPTY '<unset>'
check output '' "$output"

scenario "a value may hold = and a % that isn't an escape"
run OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318 'OTEL_EXPORTER_OTLP_HEADERS=token=YWJj==%zz%4' -- start
check_var KC_TELEMETRY_HEADER_TOKEN 'YWJj==%zz%4'

scenario "a header whose name Keycloak can't spell is skipped, naming only the header"
run OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318 'OTEL_EXPORTER_OTLP_HEADERS=x_api_key=secret-header-value,x-ok=1' -- start
check status 0 "$status"
check_var KC_TELEMETRY_HEADER_X_OK 1
check output "entrypoint: skipping OTLP header x_api_key: Keycloak can't take its name" "$output"

scenario "a header that is set wins"
run OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318 'OTEL_EXPORTER_OTLP_HEADERS=x-otlp-api-key=from-otel' \
    KC_TELEMETRY_HEADER_X_OTLP_API_KEY=explicit -- start
check_var KC_TELEMETRY_HEADER_X_OTLP_API_KEY explicit

scenario "headers without an endpoint are ignored"
run 'OTEL_EXPORTER_OTLP_HEADERS=x-otlp-api-key=abc' -- start
check_var KC_TELEMETRY_HEADER_X_OTLP_API_KEY '<unset>'

scenario "under ProtoStrap the console logger is off, so each line leaves once, by OTLP"
run PROTOSTRAP_REPLICA=keycloak-1 OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318 -- start --optimized
check_var KC_LOG_CONSOLE_LEVEL off

scenario "locally, with an endpoint, the console logger stays on"
run OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318 -- start-dev
check_var KC_LOG_CONSOLE_LEVEL '<unset>'

scenario "under ProtoStrap without an endpoint the console logger stays on"
run PROTOSTRAP_REPLICA=keycloak-1 -- start --optimized
check_var KC_LOG_CONSOLE_LEVEL '<unset>'
check_var KC_TELEMETRY_LOGS_LEVEL off

scenario "under ProtoStrap with log export turned off the console logger stays on"
run PROTOSTRAP_REPLICA=keycloak-1 OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318 KC_TELEMETRY_LOGS_LEVEL=off -- start
check_var KC_LOG_CONSOLE_LEVEL '<unset>'

scenario "a console log level that is set wins under ProtoStrap"
run PROTOSTRAP_REPLICA=keycloak-1 OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318 KC_LOG_CONSOLE_LEVEL=warn -- start
check_var KC_LOG_CONSOLE_LEVEL warn

# --- the cluster's address -----------------------------------------------------------

scenario "under ProtoStrap the cluster binds to the replica's overlay address"
run PROTOSTRAP_REPLICA=keycloak-1 -- start --optimized
check status 0 "$status"
check output '' "$output"
check_var KC_CACHE_EMBEDDED_NETWORK_BIND_ADDRESS 10.200.0.7

scenario "a bind address that is set wins"
run PROTOSTRAP_REPLICA=keycloak-1 KC_CACHE_EMBEDDED_NETWORK_BIND_ADDRESS=10.200.0.9 -- start
check_var KC_CACHE_EMBEDDED_NETWORK_BIND_ADDRESS 10.200.0.9

scenario "a replica name the overlay can't resolve leaves Keycloak's default"
run PROTOSTRAP_REPLICA=keycloak-2 -- start
check status 0 "$status"
check output 'entrypoint: keycloak-2 has no address on the overlay; the cluster binds by Keycloak'"'"'s default' "$output"
check_var KC_CACHE_EMBEDDED_NETWORK_BIND_ADDRESS '<unset>'

scenario "outside ProtoStrap the bind address is Keycloak's default"
run -- start-dev
check_var KC_CACHE_EMBEDDED_NETWORK_BIND_ADDRESS '<unset>'

# --- all together, printing nothing ------------------------------------------------

scenario "a ProtoStrap replica's start prints no value"
secret KC_BOOTSTRAP_ADMIN_CLIENT_SECRET 'admin-client-secret-value'
secret ConnectionStrings__keycloak 'Host=postgres;Port=5432;Username=keycloak;Password=db-password-value;Database=keycloak'
run ORIGIN_AUTH=https://auth.protofast.dev KC_BOOTSTRAP_ADMIN_CLIENT_ID=protofast-admin \
    PROTOSTRAP_REPLICA=keycloak-1 OTEL_EXPORTER_OTLP_ENDPOINT=http://telemetry:4318 \
    OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf OTEL_SERVICE_NAME=keycloak -- start --optimized
check status 0 "$status"
check output '' "$output"
check_var KC_BOOTSTRAP_ADMIN_CLIENT_ID protofast-admin
check_var KC_BOOTSTRAP_ADMIN_CLIENT_SECRET admin-client-secret-value
check_var KC_DB_PASSWORD db-password-value
check_var KC_DB_URL_HOST postgres
check_var KC_HOSTNAME https://auth.protofast.dev
check_var KC_TRACING_ENDPOINT http://telemetry:4318
check_var KC_LOG_CONSOLE_LEVEL off
check args $'start\n--optimized' "$(cat "$work/out/args")"

scenario "aspire run's start prints no value"
run ORIGIN_AUTH=http://auth.dev.localhost:20000 \
    'ConnectionStrings__keycloak=Host=postgres;Port=5432;Username=postgres;Password=local-pw;Database=keycloak' \
    KC_BOOTSTRAP_ADMIN_CLIENT_ID=protofast-admin KC_BOOTSTRAP_ADMIN_CLIENT_SECRET=local-secret \
    KC_SPI_EMAIL_SENDER__PROVIDER=default OTEL_EXPORTER_OTLP_ENDPOINT=http://host.docker.internal:21000 \
    OTEL_EXPORTER_OTLP_PROTOCOL=grpc 'OTEL_EXPORTER_OTLP_HEADERS=x-otlp-api-key=dashboard-key' -- start-dev
check status 0 "$status"
check output '' "$output"
check_var KC_HOSTNAME http://auth.dev.localhost:20000
check_var KC_DB_USERNAME postgres
check_var KC_SPI_EMAIL_SENDER__PROVIDER default
check_var KC_TRACING_PROTOCOL grpc
check_var KC_TELEMETRY_HEADER_X_OTLP_API_KEY dashboard-key
check_var KC_LOG_CONSOLE_LEVEL '<unset>'
check args start-dev "$(cat "$work/out/args")"

scenario "the entrypoint never turns on tracing"
check 'set -x in entrypoint.sh' '' "$(grep -nE '^[[:space:]]*set[[:space:]]+-[a-z]*x' "$entrypoint" || true)"

printf '%d checks, %d failed\n' "$checks" "$failures"
((failures == 0))
