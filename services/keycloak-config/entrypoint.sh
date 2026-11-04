#!/usr/bin/env bash
# ProtoFast's keycloak-config entrypoint: keycloak's configure task. It turns what
# ProtoStrap and the AppHost hand the container into keycloak-config-cli's settings and the
# variables the realm files substitute, then runs keycloak-config-cli, which makes the
# realms in /config/realms match their files through Keycloak's admin API:
#
#   1. Each key file in /run/secrets (ProtoStrap's secret files) becomes the variable of its
#      name, such as KC_BOOTSTRAP_ADMIN_CLIENT_SECRET or Keycloak__ClientSecretAdmin, unless
#      that variable is already set.
#   2. The login. By default, and in an account, the client credentials grant of the
#      bootstrap admin client, KC_BOOTSTRAP_ADMIN_CLIENT_ID and _SECRET, on master. With
#      KEYCLOAK_GRANTTYPE=password (aspire run only, for a local database whose master realm
#      already had an admin user when the bootstrap client came in), admin-cli's password
#      grant on master as KEYCLOAK_USER with KEYCLOAK_PASSWORD.
#   3. It requires KEYCLOAK_URL, the login's variables, ORIGIN_PROTOFAST, ORIGIN_THEPLOT,
#      ORIGIN_OFFTHELOOP, ORIGIN_ADMIN, HOST_PROTOFAST, HOST_AUTH, AUTH_URL and the five client
#      secrets, and fails naming each one that is missing.
#   4. Origins and AUTH_URL lose a trailing slash. Each realm's WebAuthn RP ID is worked out
#      from the hosts (WEBAUTHN_RP_ID, THEPLOT_WEBAUTHN_RP_ID, OFFTHELOOP_WEBAUTHN_RP_ID,
#      OPERATORS_WEBAUTHN_RP_ID).
#   5. DEV_OPERATORS, such as
#      protofast@admin.test=platform,admin-protofast;theplot@admin.test=admin-theplot, adds
#      a file of users for the operators realm, imported after the realms.
#   6. Each variable the realm files substitute is escaped for a JSON string, since
#      keycloak-config-cli substitutes into the text before it parses it: a quote, a
#      backslash or a line break in a value then can't break the file, or show up in a parse
#      error's message.
#
# "Already set" means set and not empty. It never prints a value. test/entrypoint_test.sh
# sources this file and calls its functions.

secrets_dir=/run/secrets
realms_dir=/config/realms
work_dir=${TMPDIR:-/tmp}/keycloak-config
java_bin=java
cli_jar=/app/keycloak-config-cli.jar

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

# require fails, naming each of the variables $@ that isn't set, never a value.
require() {
    local name missing=()
    for name; do
        [[ -n ${!name-} ]] || missing+=("$name")
    done
    ((${#missing[@]} == 0)) && return 0
    local IFS=' '
    echo "entrypoint: missing ${missing[*]}" >&2
    return 1
}

# configure_login sets keycloak-config-cli's login and checks that every variable the task
# needs is set, naming each one that isn't. The grant is client_credentials unless
# KEYCLOAK_GRANTTYPE says password; any other grant fails.
configure_login() {
    local grant=${KEYCLOAK_GRANTTYPE:-client_credentials}
    local -a login
    case $grant in
        client_credentials) login=(KC_BOOTSTRAP_ADMIN_CLIENT_ID KC_BOOTSTRAP_ADMIN_CLIENT_SECRET) ;;
        # admin-cli, keycloak-config-cli's default client.
        password) login=(KEYCLOAK_USER KEYCLOAK_PASSWORD) ;;
        *)
            echo "entrypoint: KEYCLOAK_GRANTTYPE is $grant; it must be client_credentials or password" >&2
            return 1
            ;;
    esac
    require KEYCLOAK_URL "${login[@]}" \
        ORIGIN_PROTOFAST ORIGIN_THEPLOT ORIGIN_OFFTHELOOP ORIGIN_ADMIN HOST_PROTOFAST HOST_AUTH AUTH_URL \
        Keycloak__ClientSecretProtofastWeb Keycloak__ClientSecretTheplotWeb Keycloak__ClientSecretOfftheloopWeb \
        Keycloak__ClientSecretAdmin Keycloak__AdminClientSecret || return 1
    export KEYCLOAK_GRANTTYPE=$grant
    set_default KEYCLOAK_LOGINREALM master
    if [[ $grant == client_credentials ]]; then
        set_default KEYCLOAK_CLIENTID "$KC_BOOTSTRAP_ADMIN_CLIENT_ID"
        set_default KEYCLOAK_CLIENTSECRET "$KC_BOOTSTRAP_ADMIN_CLIENT_SECRET"
    fi
}

# trim_urls drops the trailing slashes of the origins and AUTH_URL, which the realm files
# follow with a path.
trim_urls() {
    local name value
    for name in ORIGIN_PROTOFAST ORIGIN_THEPLOT ORIGIN_OFFTHELOOP ORIGIN_ADMIN AUTH_URL; do
        value=${!name-}
        while [[ $value == */ ]]; do
            value=${value%/}
        done
        export "$name=$value"
    done
}

# set_webauthn_rp_ids sets each realm's WebAuthn RP ID, unless it is set. The RP ID must be
# the host of the page that runs the ceremony, Keycloak's (HOST_AUTH), or a parent of it.
# protofast's is HOST_PROTOFAST when that is a parent of HOST_AUTH (protofast.dev for
# auth.protofast.dev), so its passkeys are ProtoFast's own; otherwise, as locally
# (auth.dev.localhost isn't under protofast.dev.localhost), HOST_AUTH. theplot's, offtheloop's
# and operators' are HOST_AUTH, so no realm's passkeys show up in another's picker.
set_webauthn_rp_ids() {
    local auth=${HOST_AUTH,,} protofast=${HOST_PROTOFAST,,} rp_id
    if [[ $auth == *".$protofast" ]]; then
        rp_id=$protofast
    else
        rp_id=$auth
    fi
    set_default WEBAUTHN_RP_ID "$rp_id"
    set_default THEPLOT_WEBAUTHN_RP_ID "$auth"
    set_default OFFTHELOOP_WEBAUTHN_RP_ID "$auth"
    set_default OPERATORS_WEBAUTHN_RP_ID "$auth"
}

# trim prints $1 without leading or trailing whitespace.
trim() {
    local text=$1
    text=${text#"${text%%[![:space:]]*}"}
    printf '%s' "${text%"${text##*[![:space:]]}"}"
}

# dev_operators_json prints the operators realm's users that DEV_OPERATORS, $1, names:
# entries separated by ;, each an email, =, and its realm roles separated by commas. Each
# user is enabled, has a verified email and holds exactly the roles listed (and the realm's
# default roles), with no password, since every operator signs in with an emailed code,
# and no required action (Keycloak gives a new user the realm's default one, which the
# next run clears). It fails, naming the entry, for one it can't read.
dev_operators_json() {
    local spec=$1 entry email roles role users='' role_list
    local -a entries role_names
    local -A seen=()
    IFS=';' read -r -a entries <<<"$spec"
    for entry in "${entries[@]}"; do
        entry=$(trim "$entry")
        [[ -n $entry ]] || continue
        if [[ $entry != *=* ]]; then
            echo "entrypoint: DEV_OPERATORS entry '$entry' isn't email=role[,role...]" >&2
            return 1
        fi
        email=$(trim "${entry%%=*}")
        email=${email,,}
        roles=${entry#*=}
        if [[ ! $email =~ ^[a-z0-9._%+-]+@[a-z0-9-]+(\.[a-z0-9-]+)*$ ]]; then
            echo "entrypoint: DEV_OPERATORS entry '$entry' doesn't start with an email address" >&2
            return 1
        fi
        if [[ -n ${seen[$email]-} ]]; then
            echo "entrypoint: DEV_OPERATORS names $email twice" >&2
            return 1
        fi
        seen[$email]=1
        IFS=',' read -r -a role_names <<<"$roles"
        role_list=''
        for role in "${role_names[@]}"; do
            role=$(trim "$role")
            [[ -n $role ]] || continue
            if [[ ! $role =~ ^[A-Za-z0-9._-]+$ ]]; then
                echo "entrypoint: DEV_OPERATORS entry '$entry' has a role that isn't a role name: '$role'" >&2
                return 1
            fi
            role_list+=${role_list:+,}\"$role\"
        done
        if [[ -z $role_list ]]; then
            echo "entrypoint: DEV_OPERATORS entry '$entry' names no role" >&2
            return 1
        fi
        users+=${users:+,}$'\n    '
        users+="{\"username\": \"$email\", \"email\": \"$email\", \"emailVerified\": true, \"enabled\": true, \"requiredActions\": [], \"realmRoles\": [$role_list]}"
    done
    if [[ -z $users ]]; then
        echo "entrypoint: DEV_OPERATORS names no operator" >&2
        return 1
    fi
    printf '{\n  "realm": "operators",\n  "users": [%s\n  ]\n}\n' "$users"
}

# realm_variables prints, once each, the name of every variable the files in the directory
# $1 substitute: each $(env:NAME...), including those inside another's default.
realm_variables() {
    local dir=$1 file line rest name
    local pattern='\$\(env:([A-Za-z_][A-Za-z0-9_]*)(.*)$'
    local -A seen=()
    for file in "$dir"/*.json; do
        [[ -f $file ]] || continue
        while IFS= read -r line || [[ -n $line ]]; do
            rest=$line
            while [[ $rest =~ $pattern ]]; do
                name=${BASH_REMATCH[1]}
                rest=${BASH_REMATCH[2]}
                [[ -n ${seen[$name]-} ]] && continue
                seen[$name]=1
                printf '%s\n' "$name"
            done
        done <"$file"
    done
}

# json_string_escape prints $1 as it must appear inside a JSON string that
# keycloak-config-cli substitutes into: \ and " escaped, control characters as \n, \r, \t
# or \u00XX, and $( doubled to $$(, so the value is never taken for a variable.
json_string_escape() {
    local text=$1 code char escaped
    text=${text//\\/\\\\}
    text=${text//\"/\\\"}
    text=${text//$'\n'/\\n}
    text=${text//$'\r'/\\r}
    text=${text//$'\t'/\\t}
    for ((code = 1; code < 32; code++)); do
        # shellcheck disable=SC2059 # The format is the escape \xXX itself.
        printf -v char "\\x$(printf '%02x' "$code")"
        [[ $text == *"$char"* ]] || continue
        printf -v escaped '\\u%04x' "$code"
        text=${text//"$char"/$escaped}
    done
    # shellcheck disable=SC2016 # A literal $(, not an expansion.
    text=${text//'$('/'$$('}
    printf '%s' "$text"
}

# escape_realm_values escapes, in place, each variable the realm files in $1 substitute.
escape_realm_values() {
    local name names
    names=$(realm_variables "$1")
    while IFS= read -r name; do
        [[ -n $name && -n ${!name-} ]] || continue
        export "$name=$(json_string_escape "${!name}")"
    done <<<"$names"
}

main() {
    load_secret_files "$secrets_dir"
    configure_login || exit 1
    trim_urls
    set_webauthn_rp_ids

    local locations="file:$realms_dir/*.json" json
    if [[ -n ${DEV_OPERATORS-} ]]; then
        json=$(dev_operators_json "$DEV_OPERATORS") || exit 1
        mkdir -p "$work_dir"
        printf '%s\n' "$json" >"$work_dir/dev-operators.json"
        locations+=",file:$work_dir/dev-operators.json"
    fi
    export IMPORT_FILES_LOCATIONS=$locations

    # Under ProtoStrap, JSON lines on stdout, which Loki can filter and parse.
    [[ -n ${PROTOSTRAP_REPLICA-} ]] && set_default SPRING_PROFILES_ACTIVE json-log

    escape_realm_values "$realms_dir"
    # JAVA_OPTS is split into words on purpose, as the base image's entrypoint does.
    # shellcheck disable=SC2086
    exec "$java_bin" ${JAVA_OPTS-} -jar "$cli_jar"
}

if [[ ${BASH_SOURCE[0]} == "$0" ]]; then
    set -euo pipefail
    main "$@"
fi
