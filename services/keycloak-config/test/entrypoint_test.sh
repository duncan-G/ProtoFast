#!/usr/bin/env bash
# Tests for ../entrypoint.sh. Each runs the entrypoint's main in a clean environment
# (env -i), with its secrets directory, realm files and work directory in a temporary
# directory and java replaced by a stub that records its arguments and exported variables.
#
#   bash test/entrypoint_test.sh                 # from services/keycloak-config, bash 4.4 or later
#
# or in a container, since Docker Desktop can't mount every directory:
#
#   tar c entrypoint.sh realms test | docker run --rm -i bash:5 \
#     bash -c 'mkdir /k && cd /k && tar x && bash test/entrypoint_test.sh'
#
# It prints one line per failed check and a summary, and exits 1 if any failed.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
entrypoint=$here/../entrypoint.sh
real_realms=$here/../realms
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

checks=0
failures=0
current=''

# The stub java writes its arguments, one per line, and `export -p` to files.
cat >"$work/java" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$@" >"$STUB_DIR/args"
export -p >"$STUB_DIR/env"
STUB
chmod +x "$work/java"

# The realm files the tests use by default: one that substitutes a few variables, one
# inside another's default.
make_realms() {
    rm -rf "$work/realms"
    mkdir -p "$work/realms"
    cat >"$work/realms/a-realm.json" <<'JSON'
{
  "realm": "a",
  "smtpServer": { "from": "$(env:SMTP_FROM:-no-reply@$(env:HOST_PROTOFAST))", "password": "$(env:SMTP_PASSWORD:-)" },
  "clients": [ { "clientId": "web", "secret": "$(env:Keycloak__ClientSecretProtofastWeb)", "baseUrl": "$(env:ORIGIN_PROTOFAST)" } ]
}
JSON
    cat >"$work/realms/b-realm.json" <<'JSON'
{ "realm": "b", "identityProviders": [ { "config": { "p8PrivateKey": "$(env:APPLE_PRIVATE_KEY:-)", "clientId": "$(env:GOOGLE_CLIENT_ID:-)" } } ] }
JSON
}

# scenario starts a test: fresh secrets, realm files, work directory and stub output.
scenario() {
    current=$1
    rm -rf "$work/secrets" "$work/out" "$work/tmp"
    mkdir -p "$work/out" "$work/tmp"
    secrets=$work/secrets
    realms=$work/realms
    make_realms
}

# The variables a run needs, all set; a test leaves one out with `without`.
required=(
    KEYCLOAK_URL=http://keycloak:8080
    KC_BOOTSTRAP_ADMIN_CLIENT_ID=protofast-admin
    KC_BOOTSTRAP_ADMIN_CLIENT_SECRET=bootstrap-secret-value
    ORIGIN_PROTOFAST=https://protofast.dev
    ORIGIN_THEPLOT=https://theplot.protofast.dev
    ORIGIN_OFFTHELOOP=https://offtheloop.protofast.dev
    ORIGIN_ADMIN=https://admin.protofast.dev
    HOST_PROTOFAST=protofast.dev
    HOST_AUTH=auth.protofast.dev
    AUTH_URL=http://auth:8080
    Keycloak__ClientSecretProtofastWeb=protofast-web-secret-value
    Keycloak__ClientSecretTheplotWeb=theplot-web-secret-value
    Keycloak__ClientSecretOfftheloopWeb=offtheloop-web-secret-value
    Keycloak__ClientSecretAdmin=admin-secret-value
    Keycloak__AdminClientSecret=account-admin-secret-value
)

# without prints the required variables but those named.
without() {
    local entry name skip
    for entry in "${required[@]}"; do
        skip=0
        for name; do
            [[ ${entry%%=*} == "$name" ]] && skip=1
        done
        ((skip)) || printf '%s\n' "$entry"
    done
}

# run runs the entrypoint's main with only the given NAME=value variables. It keeps its
# exit status in status and what it printed in output.
run() {
    # shellcheck disable=SC2016 # The inner script expands its own arguments.
    output=$(env -i PATH="$PATH" STUB_DIR="$work/out" TMPDIR="$work/tmp" "$@" bash -c '
        source "$1"
        secrets_dir=$2
        realms_dir=$3
        java_bin=$4
        work_dir=$5
        set -euo pipefail
        main' entrypoint "$entrypoint" "$secrets" "$realms" "$work/java" "$work/tmp/keycloak-config" 2>&1)
    status=$?
}

# run_all runs with every required variable and the given ones.
run_all() {
    local -a vars
    mapfile -t vars < <(without)
    run "${vars[@]}" "$@"
}

# run_without runs with every required variable but those named before --, and the
# variables after it.
run_without() {
    local -a names=() vars
    while (($#)) && [[ $1 != -- ]]; do
        names+=("$1")
        shift
    done
    (($#)) && shift
    mapfile -t vars < <(without "${names[@]}")
    run "${vars[@]}" "$@"
}

# got prints the variable $1 as java saw it, or <unset>.
got() {
    [[ -f $work/out/env ]] || {
        printf '<java not run>'
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

java_ran() {
    [[ -f $work/out/args ]] && echo yes || echo no
}

# secret writes a key file as the agent does: no trailing newline.
secret() {
    mkdir -p "$secrets"
    printf '%s' "$2" >"$secrets/$1"
}

# --- running keycloak-config-cli -----------------------------------------------------

scenario "it runs keycloak-config-cli's JAR"
run_all
check status 0 "$status"
check args $'-jar\n/app/keycloak-config-cli.jar' "$(cat "$work/out/args")"
check output '' "$output"

scenario "JAVA_OPTS are split into words, as the base image's entrypoint does"
run_all 'JAVA_OPTS=-Xmx256m -XX:+UseSerialGC'
check args $'-Xmx256m\n-XX:+UseSerialGC\n-jar\n/app/keycloak-config-cli.jar' "$(cat "$work/out/args")"

scenario "the realm files are the files to import"
run_all
check_var IMPORT_FILES_LOCATIONS "file:$work/realms/*.json"

# --- the login -------------------------------------------------------------------------

scenario "by default it logs in to master with the bootstrap admin client's credentials"
run_all
check_var KEYCLOAK_GRANTTYPE client_credentials
check_var KEYCLOAK_CLIENTID protofast-admin
check_var KEYCLOAK_CLIENTSECRET bootstrap-secret-value
check_var KEYCLOAK_LOGINREALM master
check_var KEYCLOAK_URL http://keycloak:8080

scenario "KEYCLOAK_GRANTTYPE=client_credentials is the same"
run_all KEYCLOAK_GRANTTYPE=client_credentials
check status 0 "$status"
check_var KEYCLOAK_CLIENTID protofast-admin

scenario "a login realm that is set is kept"
run_all KEYCLOAK_LOGINREALM=other
check_var KEYCLOAK_LOGINREALM other

scenario "the password grant logs in to master with admin-cli as KEYCLOAK_USER"
run_without KC_BOOTSTRAP_ADMIN_CLIENT_ID KC_BOOTSTRAP_ADMIN_CLIENT_SECRET -- \
    KEYCLOAK_GRANTTYPE=password KEYCLOAK_USER=admin KEYCLOAK_PASSWORD=admin-password-value
check status 0 "$status"
check_var KEYCLOAK_GRANTTYPE password
check_var KEYCLOAK_USER admin
check_var KEYCLOAK_PASSWORD admin-password-value
check_var KEYCLOAK_LOGINREALM master
check_var KEYCLOAK_CLIENTID '<unset>'
check_var KEYCLOAK_CLIENTSECRET '<unset>'
check output '' "$output"

scenario "the password grant doesn't use the bootstrap client, even when it is set"
run_all KEYCLOAK_GRANTTYPE=password KEYCLOAK_USER=admin KEYCLOAK_PASSWORD=admin-password-value
check status 0 "$status"
check_var KEYCLOAK_CLIENTID '<unset>'
check_var KEYCLOAK_CLIENTSECRET '<unset>'

scenario "the password grant needs KEYCLOAK_USER and KEYCLOAK_PASSWORD"
run_without KC_BOOTSTRAP_ADMIN_CLIENT_ID KC_BOOTSTRAP_ADMIN_CLIENT_SECRET -- KEYCLOAK_GRANTTYPE=password
check status 1 "$status"
check 'java ran' no "$(java_ran)"
check output 'entrypoint: missing KEYCLOAK_USER KEYCLOAK_PASSWORD' "$output"

scenario "the password grant's password can come from a secret file"
secret KEYCLOAK_PASSWORD 'from-file'
run_without KC_BOOTSTRAP_ADMIN_CLIENT_ID KC_BOOTSTRAP_ADMIN_CLIENT_SECRET -- KEYCLOAK_GRANTTYPE=password KEYCLOAK_USER=admin
check status 0 "$status"
check_var KEYCLOAK_PASSWORD from-file

scenario "client credentials need the bootstrap client, not KEYCLOAK_USER"
run_without KC_BOOTSTRAP_ADMIN_CLIENT_SECRET -- KEYCLOAK_USER=admin KEYCLOAK_PASSWORD=pw
check status 1 "$status"
check 'java ran' no "$(java_ran)"
check output 'entrypoint: missing KC_BOOTSTRAP_ADMIN_CLIENT_SECRET' "$output"

scenario "another grant type fails, naming it"
run_all KEYCLOAK_GRANTTYPE=refresh_token
check status 1 "$status"
check 'java ran' no "$(java_ran)"
check output 'entrypoint: KEYCLOAK_GRANTTYPE is refresh_token; it must be client_credentials or password' "$output"

# --- what it requires ------------------------------------------------------------------

for name in KEYCLOAK_URL KC_BOOTSTRAP_ADMIN_CLIENT_ID KC_BOOTSTRAP_ADMIN_CLIENT_SECRET ORIGIN_PROTOFAST \
    ORIGIN_THEPLOT ORIGIN_OFFTHELOOP ORIGIN_ADMIN HOST_PROTOFAST HOST_AUTH AUTH_URL Keycloak__ClientSecretProtofastWeb \
    Keycloak__ClientSecretTheplotWeb Keycloak__ClientSecretOfftheloopWeb Keycloak__ClientSecretAdmin \
    Keycloak__AdminClientSecret; do
    scenario "without $name it fails, naming it and no value"
    run_without "$name"
    check status 1 "$status"
    check 'java ran' no "$(java_ran)"
    check output "entrypoint: missing $name" "$output"
done

scenario "an empty variable counts as missing"
run_all ORIGIN_ADMIN=
check status 1 "$status"
check output 'entrypoint: missing ORIGIN_ADMIN' "$output"

scenario "every missing variable is named at once"
run
check status 1 "$status"
check output 'entrypoint: missing KEYCLOAK_URL KC_BOOTSTRAP_ADMIN_CLIENT_ID KC_BOOTSTRAP_ADMIN_CLIENT_SECRET ORIGIN_PROTOFAST ORIGIN_THEPLOT ORIGIN_OFFTHELOOP ORIGIN_ADMIN HOST_PROTOFAST HOST_AUTH AUTH_URL Keycloak__ClientSecretProtofastWeb Keycloak__ClientSecretTheplotWeb Keycloak__ClientSecretOfftheloopWeb Keycloak__ClientSecretAdmin Keycloak__AdminClientSecret' "$output"

scenario "Google's and Apple's keys and SMTP's may be missing or empty"
run_all GOOGLE_CLIENT_ID= APPLE_PRIVATE_KEY=
check status 0 "$status"
check_var GOOGLE_CLIENT_ID ''
check_var SMTP_HOST '<unset>'

# --- secret files ----------------------------------------------------------------------

scenario "each secret file becomes the variable of its name"
secret KC_BOOTSTRAP_ADMIN_CLIENT_SECRET 'from-file'
secret Keycloak__ClientSecretAdmin 'admin-from-file'
run_without KC_BOOTSTRAP_ADMIN_CLIENT_SECRET Keycloak__ClientSecretAdmin
check status 0 "$status"
check_var KEYCLOAK_CLIENTSECRET from-file
check_var Keycloak__ClientSecretAdmin admin-from-file
check output '' "$output"

scenario "a variable that is already set wins over its file"
secret Keycloak__ClientSecretAdmin 'from-file'
run_all
check_var Keycloak__ClientSecretAdmin admin-secret-value

scenario "the agent's layout: links to ..data, and previous/ skipped"
mkdir -p "$secrets/..v-2" "$secrets/..v-1"
printf '%s' 'new-secret' >"$secrets/..v-2/Keycloak__AdminClientSecret"
printf '%s' 'old-secret' >"$secrets/..v-1/Keycloak__AdminClientSecret"
ln -s ..v-2 "$secrets/..data"
ln -s ..v-1 "$secrets/previous"
ln -s ..data/Keycloak__AdminClientSecret "$secrets/Keycloak__AdminClientSecret"
run_without Keycloak__AdminClientSecret
check status 0 "$status"
check_var Keycloak__AdminClientSecret new-secret
check_var previous '<unset>'
check output '' "$output"

scenario "a file whose name isn't a variable name is skipped, naming only the file"
secret 'tls.key' 'key-body'
run_all
check status 0 "$status"
check output 'entrypoint: skipping secret file tls.key: not a variable name' "$output"

scenario "a secret file's value may be empty, as Google's and Apple's may"
secret GOOGLE_CLIENT_SECRET ''
run_all
check status 0 "$status"
check_var GOOGLE_CLIENT_SECRET '<unset>'

# --- URLs and WebAuthn RP IDs --------------------------------------------------------

scenario "origins and AUTH_URL lose their trailing slashes"
run_all ORIGIN_PROTOFAST=https://protofast.dev/ ORIGIN_THEPLOT=https://theplot.protofast.dev// \
    ORIGIN_OFFTHELOOP=https://offtheloop.protofast.dev/ ORIGIN_ADMIN=http://admin.dev.localhost:20000/ \
    AUTH_URL=http://auth:8080/
check_var ORIGIN_PROTOFAST https://protofast.dev
check_var ORIGIN_THEPLOT https://theplot.protofast.dev
check_var ORIGIN_OFFTHELOOP https://offtheloop.protofast.dev
check_var ORIGIN_ADMIN http://admin.dev.localhost:20000
check_var AUTH_URL http://auth:8080

scenario "in production protofast's RP ID is its own domain, the parent of Keycloak's host"
run_all HOST_PROTOFAST=protofast.dev HOST_AUTH=auth.protofast.dev
check_var WEBAUTHN_RP_ID protofast.dev
check_var THEPLOT_WEBAUTHN_RP_ID auth.protofast.dev
check_var OFFTHELOOP_WEBAUTHN_RP_ID auth.protofast.dev
check_var OPERATORS_WEBAUTHN_RP_ID auth.protofast.dev

scenario "locally Keycloak's host isn't under protofast's, so every RP ID is Keycloak's host"
run_all HOST_PROTOFAST=protofast.dev.localhost HOST_AUTH=auth.dev.localhost
check_var WEBAUTHN_RP_ID auth.dev.localhost
check_var THEPLOT_WEBAUTHN_RP_ID auth.dev.localhost
check_var OFFTHELOOP_WEBAUTHN_RP_ID auth.dev.localhost
check_var OPERATORS_WEBAUTHN_RP_ID auth.dev.localhost

scenario "a host that only ends with protofast's name isn't under it"
run_all HOST_PROTOFAST=protofast.dev HOST_AUTH=authprotofast.dev
check_var WEBAUTHN_RP_ID authprotofast.dev

scenario "Keycloak on protofast's own host gets that host"
run_all HOST_PROTOFAST=protofast.dev HOST_AUTH=protofast.dev
check_var WEBAUTHN_RP_ID protofast.dev

scenario "hosts are compared, and RP IDs written, in lower case"
run_all HOST_PROTOFAST=ProtoFast.Dev HOST_AUTH=Auth.ProtoFast.Dev
check_var WEBAUTHN_RP_ID protofast.dev
check_var THEPLOT_WEBAUTHN_RP_ID auth.protofast.dev

scenario "an RP ID that is set wins"
run_all WEBAUTHN_RP_ID=localhost THEPLOT_WEBAUTHN_RP_ID=theplot.example OFFTHELOOP_WEBAUTHN_RP_ID=loop.example \
    OPERATORS_WEBAUTHN_RP_ID=ops.example
check_var WEBAUTHN_RP_ID localhost
check_var THEPLOT_WEBAUTHN_RP_ID theplot.example
check_var OFFTHELOOP_WEBAUTHN_RP_ID loop.example
check_var OPERATORS_WEBAUTHN_RP_ID ops.example

# --- dev operators -------------------------------------------------------------------

scenario "without DEV_OPERATORS there is no dev operators file"
run_all
check 'dev file' no "$([[ -e $work/tmp/keycloak-config/dev-operators.json ]] && echo yes || echo no)"

scenario "an empty DEV_OPERATORS is no dev operators file"
run_all DEV_OPERATORS=
check status 0 "$status"
check_var IMPORT_FILES_LOCATIONS "file:$work/realms/*.json"

scenario "DEV_OPERATORS adds the operators realm's users, imported after the realms"
run_all 'DEV_OPERATORS=protofast@admin.test=platform,admin-protofast;theplot@admin.test=admin-theplot'
check status 0 "$status"
check output '' "$output"
check_var IMPORT_FILES_LOCATIONS "file:$work/realms/*.json,file:$work/tmp/keycloak-config/dev-operators.json"
check 'dev operators file' '{
  "realm": "operators",
  "users": [
    {"username": "protofast@admin.test", "email": "protofast@admin.test", "emailVerified": true, "enabled": true, "requiredActions": [], "realmRoles": ["platform","admin-protofast"]},
    {"username": "theplot@admin.test", "email": "theplot@admin.test", "emailVerified": true, "enabled": true, "requiredActions": [], "realmRoles": ["admin-theplot"]}
  ]
}' "$(cat "$work/tmp/keycloak-config/dev-operators.json")"

scenario "dev operators: spaces, an empty entry and a trailing ; are fine, and emails are lower-cased"
run_all 'DEV_OPERATORS= Ops@Admin.Test = platform , admin-theplot ;; '
check status 0 "$status"
check 'dev operators file' '{
  "realm": "operators",
  "users": [
    {"username": "ops@admin.test", "email": "ops@admin.test", "emailVerified": true, "enabled": true, "requiredActions": [], "realmRoles": ["platform","admin-theplot"]}
  ]
}' "$(cat "$work/tmp/keycloak-config/dev-operators.json")"

scenario "a dev operator without = fails, naming the entry"
run_all 'DEV_OPERATORS=ops@admin.test'
check status 1 "$status"
check 'java ran' no "$(java_ran)"
check output "entrypoint: DEV_OPERATORS entry 'ops@admin.test' isn't email=role[,role...]" "$output"

scenario "a dev operator without a role fails"
run_all 'DEV_OPERATORS=ops@admin.test= , '
check status 1 "$status"
check output "entrypoint: DEV_OPERATORS entry 'ops@admin.test= ,' names no role" "$output"

scenario "a dev operator whose email isn't one fails"
run_all 'DEV_OPERATORS=not an "email"=platform'
check status 1 "$status"
check output "entrypoint: DEV_OPERATORS entry 'not an \"email\"=platform' doesn't start with an email address" "$output"

scenario "a role that isn't a role name fails"
run_all 'DEV_OPERATORS=ops@admin.test=platform,"x"'
check status 1 "$status"
check output "entrypoint: DEV_OPERATORS entry 'ops@admin.test=platform,\"x\"' has a role that isn't a role name: '\"x\"'" "$output"

scenario "the same dev operator twice fails"
run_all 'DEV_OPERATORS=ops@admin.test=platform;OPS@admin.test=admin-theplot'
check status 1 "$status"
check output 'entrypoint: DEV_OPERATORS names ops@admin.test twice' "$output"

scenario "DEV_OPERATORS of only separators names no operator"
run_all 'DEV_OPERATORS=; ;'
check status 1 "$status"
check output 'entrypoint: DEV_OPERATORS names no operator' "$output"

# --- values in the realm files -------------------------------------------------------

scenario "realm_variables names each variable the files substitute, once, defaults' too"
# shellcheck disable=SC1090,SC1091 # The entrypoint, sourced for its functions.
check names $'APPLE_PRIVATE_KEY\nGOOGLE_CLIENT_ID\nHOST_PROTOFAST\nKeycloak__ClientSecretProtofastWeb\nORIGIN_PROTOFAST\nSMTP_FROM\nSMTP_PASSWORD' \
    "$(source "$entrypoint" && realm_variables "$work/realms" | sort)"

scenario "the real realm files substitute only what the entrypoint requires, works out, or defaults"
expected=$'APPLE_CLIENT_ID\nAPPLE_KEY_ID\nAPPLE_PRIVATE_KEY\nAPPLE_TEAM_ID\nAUTH_URL\nGOOGLE_CLIENT_ID\nGOOGLE_CLIENT_SECRET\nHOST_PROTOFAST\nKeycloak__AdminClientSecret\nKeycloak__ClientSecretAdmin\nKeycloak__ClientSecretOfftheloopWeb\nKeycloak__ClientSecretProtofastWeb\nKeycloak__ClientSecretTheplotWeb\nOFFTHELOOP_WEBAUTHN_RP_ID\nOPERATORS_WEBAUTHN_RP_ID\nORIGIN_ADMIN\nORIGIN_OFFTHELOOP\nORIGIN_PROTOFAST\nORIGIN_THEPLOT\nSMTP_AUTH\nSMTP_FROM\nSMTP_HOST\nSMTP_PASSWORD\nSMTP_PORT\nSMTP_SSL\nSMTP_STARTTLS\nSMTP_USER\nTHEPLOT_WEBAUTHN_RP_ID\nWEBAUTHN_RP_ID'
# shellcheck disable=SC1090,SC1091 # The entrypoint, sourced for its functions.
check names "$expected" "$(source "$entrypoint" && realm_variables "$real_realms" | LC_ALL=C sort)"
# Each one without a default is required or worked out by the entrypoint.
no_default=$(grep -ohE '\$\(env:[A-Za-z_][A-Za-z0-9_]*\)' "$real_realms"/*.json | sed -E 's/^\$\(env:(.*)\)$/\1/' | LC_ALL=C sort -u)
check 'without a default' $'AUTH_URL\nHOST_PROTOFAST\nKeycloak__AdminClientSecret\nKeycloak__ClientSecretAdmin\nKeycloak__ClientSecretOfftheloopWeb\nKeycloak__ClientSecretProtofastWeb\nKeycloak__ClientSecretTheplotWeb\nOFFTHELOOP_WEBAUTHN_RP_ID\nOPERATORS_WEBAUTHN_RP_ID\nORIGIN_ADMIN\nORIGIN_OFFTHELOOP\nORIGIN_PROTOFAST\nORIGIN_THEPLOT\nTHEPLOT_WEBAUTHN_RP_ID\nWEBAUTHN_RP_ID' "$no_default"
check "no Keycloak \${...} placeholder left" '' "$(grep -lE '\$\{[^}]*\}' "$real_realms"/*.json || true)"

scenario "values are escaped for a JSON string: quotes, backslashes, line breaks, tabs"
run_all 'Keycloak__ClientSecretProtofastWeb=a"b\c' $'APPLE_PRIVATE_KEY=-----BEGIN-----\nMIGT\tAg\r\n-----END-----' \
    $'SMTP_PASSWORD=bell\x07esc\x1bend'
check status 0 "$status"
check_var Keycloak__ClientSecretProtofastWeb 'a\"b\\c'
check_var APPLE_PRIVATE_KEY '-----BEGIN-----\nMIGT\tAg\r\n-----END-----'
check_var SMTP_PASSWORD 'bell\u0007esc\u001bend'
check output '' "$output"

scenario "\$( in a value is doubled, so keycloak-config-cli never takes it for a variable"
# shellcheck disable=SC2016 # Literal $( and $HOME, as a secret may hold them.
run_all 'GOOGLE_CLIENT_ID=id-$(env:HOME)-$$(x)-$HOME-:-'
# shellcheck disable=SC2016
check_var GOOGLE_CLIENT_ID 'id-$$(env:HOME)-$$$(x)-$HOME-:-'

scenario "a value without such characters is unchanged, as is a variable the files don't name"
# shellcheck disable=SC1003 # A value that ends in a backslash.
run_all 'ORIGIN_PROTOFAST=https://protofast.dev' 'KC_BOOTSTRAP_ADMIN_CLIENT_SECRET=raw"secret\' 'UNRELATED=a"b'
check_var ORIGIN_PROTOFAST https://protofast.dev
# shellcheck disable=SC1003
check_var KEYCLOAK_CLIENTSECRET 'raw"secret\'
# shellcheck disable=SC1003
check_var KC_BOOTSTRAP_ADMIN_CLIENT_SECRET 'raw"secret\'
check_var UNRELATED 'a"b'

scenario "a value from a secret file is escaped too"
secret APPLE_PRIVATE_KEY $'line one\nline "two"'
run_all
check_var APPLE_PRIVATE_KEY 'line one\nline \"two\"'

scenario "the RP IDs are worked out from the hosts before escaping"
run_all HOST_PROTOFAST=protofast.dev HOST_AUTH=auth.protofast.dev
check_var HOST_PROTOFAST protofast.dev
check_var WEBAUTHN_RP_ID protofast.dev

# --- logs ------------------------------------------------------------------------------

scenario "under ProtoStrap keycloak-config-cli logs JSON lines"
run_all PROTOSTRAP_REPLICA=keycloak-config-1
check_var SPRING_PROFILES_ACTIVE json-log

scenario "locally it logs plain lines"
run_all
check_var SPRING_PROFILES_ACTIVE '<unset>'

scenario "a Spring profile that is set wins under ProtoStrap"
run_all PROTOSTRAP_REPLICA=keycloak-config-1 SPRING_PROFILES_ACTIVE=debug
check_var SPRING_PROFILES_ACTIVE debug

# --- all together, printing nothing ------------------------------------------------

scenario "a ProtoStrap run, with every secret from a file, prints no value"
realms=$real_realms
for key in KC_BOOTSTRAP_ADMIN_CLIENT_SECRET Keycloak__ClientSecretProtofastWeb Keycloak__ClientSecretTheplotWeb \
    Keycloak__ClientSecretOfftheloopWeb Keycloak__ClientSecretAdmin Keycloak__AdminClientSecret GOOGLE_CLIENT_ID GOOGLE_CLIENT_SECRET \
    APPLE_CLIENT_ID APPLE_TEAM_ID APPLE_KEY_ID APPLE_PRIVATE_KEY; do
    secret "$key" "$key-value"
done
run KEYCLOAK_URL=http://keycloak:8080 KC_BOOTSTRAP_ADMIN_CLIENT_ID=protofast-admin PROTOSTRAP_REPLICA=keycloak-config-1 \
    ORIGIN_PROTOFAST=https://protofast.dev ORIGIN_THEPLOT=https://theplot.protofast.dev \
    ORIGIN_OFFTHELOOP=https://offtheloop.protofast.dev ORIGIN_ADMIN=https://admin.protofast.dev \
    HOST_PROTOFAST=protofast.dev HOST_AUTH=auth.protofast.dev AUTH_URL=http://auth:8080
check status 0 "$status"
check output '' "$output"
check_var KEYCLOAK_GRANTTYPE client_credentials
check_var KEYCLOAK_CLIENTSECRET KC_BOOTSTRAP_ADMIN_CLIENT_SECRET-value
check_var Keycloak__ClientSecretTheplotWeb Keycloak__ClientSecretTheplotWeb-value
check_var Keycloak__ClientSecretOfftheloopWeb Keycloak__ClientSecretOfftheloopWeb-value
check_var APPLE_PRIVATE_KEY APPLE_PRIVATE_KEY-value
check_var WEBAUTHN_RP_ID protofast.dev
check_var OPERATORS_WEBAUTHN_RP_ID auth.protofast.dev
check_var IMPORT_FILES_LOCATIONS "file:$real_realms/*.json"

scenario "aspire run's run, with the password grant and dev operators, prints no value"
realms=$real_realms
run KEYCLOAK_URL=http://localhost:8080 KEYCLOAK_GRANTTYPE=password KEYCLOAK_USER=admin KEYCLOAK_PASSWORD=local-admin-pw \
    ORIGIN_PROTOFAST=http://protofast.dev.localhost:20000 ORIGIN_THEPLOT=http://theplot.dev.localhost:20000 \
    ORIGIN_OFFTHELOOP=http://offtheloop.dev.localhost:20000 ORIGIN_ADMIN=http://admin.dev.localhost:20000 HOST_PROTOFAST=protofast.dev.localhost HOST_AUTH=auth.dev.localhost \
    AUTH_URL=http://host.docker.internal:5100 SMTP_HOST=smtp4dev SMTP_PORT=25 \
    Keycloak__ClientSecretProtofastWeb=a Keycloak__ClientSecretTheplotWeb=b Keycloak__ClientSecretOfftheloopWeb=e \
    Keycloak__ClientSecretAdmin=c Keycloak__AdminClientSecret=d 'DEV_OPERATORS=protofast@admin.test=platform,admin-protofast;theplot@admin.test=admin-theplot'
check status 0 "$status"
check output '' "$output"
check_var WEBAUTHN_RP_ID auth.dev.localhost
check_var KEYCLOAK_GRANTTYPE password
check 'dev operators file' yes "$([[ -s $work/tmp/keycloak-config/dev-operators.json ]] && echo yes || echo no)"

scenario "the entrypoint never turns on tracing"
check 'set -x in entrypoint.sh' '' "$(grep -nE '^[[:space:]]*set[[:space:]]+-[a-z]*x' "$entrypoint" || true)"

printf '%d checks, %d failed\n' "$checks" "$failures"
((failures == 0))
