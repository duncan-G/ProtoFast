#!/usr/bin/env python3
"""Isolation checks for an admin console project (docs/design/admin-consoles.md §5, §7).

    scripts/check-admin-console.py clients/admin-theplot

Run in the console's pipeline after `npm run generate:grpc`. Fails when:

* its generated code holds protos other than Admin/Shared and its own Admin/<App>;
* its src/admin-kit is anything but the symlink to the platform's kit;
* its sources import a package outside the allowed set, or its package.json adds a
  dependency the platform console (clients/admin) does not have and that is not approved;
* the clients host (clients/host) would load an app console in one process with another client;
* the clients host would serve the console without a CSP that pins connect-src to its own origin.

The two host checks ask clients/host/host.mjs itself, with node.
"""

import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PLATFORM = "admin"
ALLOWED_IMPORTS = re.compile(
    r"^(\.|@angular/|@connectrpc/|@bufbuild/|@opentelemetry/|rxjs|tslib$|express$|node:"
    r"|highlight\.js(/|$)|marked$)"
)
# Reviewed exceptions an app console may depend on although the platform console does not.
APPROVED_DEPENDENCIES = {"highlight.js", "marked"}
IMPORT = re.compile(r"""(?:^|\s)(?:import|export)\s[^'"]*?from\s+['"]([^'"]+)['"]|import\(\s*['"]([^'"]+)['"]\s*\)""")

failures = []


def fail(message):
    failures.append(message)


def check_generated(project, app):
    gen = project / "src" / "lib" / "gen"
    allowed = {"Admin/Shared"} | ({f"Admin/{app.capitalize()}"} if app else set())
    if not gen.is_dir():
        fail(f"{gen} is missing; run `npm run generate:grpc` first")
        return
    for file in gen.rglob("*.ts"):
        folder = file.parent.relative_to(gen).as_posix()
        if folder not in allowed:
            fail(f"generated code outside {sorted(allowed)}: {file.relative_to(ROOT)}")


def check_kit_link(project):
    link = project / "src" / "admin-kit"
    if not link.is_symlink() or link.resolve() != (ROOT / "clients" / "admin-kit" / "src"):
        fail(f"{link.relative_to(ROOT)} must be a symlink to clients/admin-kit/src")


def check_imports(project):
    src = project / "src"
    for file in src.rglob("*.ts"):
        relative = file.relative_to(src).as_posix()
        if relative.startswith(("admin-kit/", "lib/gen/")):
            continue
        for match in IMPORT.finditer(file.read_text()):
            specifier = match.group(1) or match.group(2)
            if not ALLOWED_IMPORTS.match(specifier):
                fail(f"{file.relative_to(ROOT)} imports {specifier!r}, which is not an allowed package")


def check_dependencies(project):
    if project.name == PLATFORM:
        return
    platform = json.loads((ROOT / "clients" / PLATFORM / "package.json").read_text())
    console = json.loads((project / "package.json").read_text())
    for section in ("dependencies", "devDependencies"):
        extra = sorted(
            set(console.get(section, {})) - set(platform.get(section, {})) - APPROVED_DEPENDENCIES
        )
        if extra:
            fail(f"{project.name} adds {section} the platform console lacks: {', '.join(extra)}")


HOST = ROOT / "clients" / "host" / "host.mjs"


def ask_host(script):
    """Runs an ES module script against the clients host's host.mjs (as `host`); its stdout."""
    node = shutil.which("node")
    if node is None:
        fail("node is not on PATH; the clients host checks need it")
        return None
    program = f"const host = await import({json.dumps(HOST.as_uri())});\n{script}"
    result = subprocess.run(
        [node, "--input-type=module", "-e", program], capture_output=True, text=True, cwd=ROOT
    )
    if result.returncode != 0:
        fail(f"asking {HOST.relative_to(ROOT)} failed: {result.stderr.strip()}")
        return None
    return result.stdout


def check_shared_host(name):
    """The host refuses to load an app console beside the platform console or another app's."""
    if name == PLATFORM:
        return
    refused = ask_host(
        "const refused = [];\n"
        f"for (const clients of [{json.dumps(f'{PLATFORM},{name}')}, {json.dumps(f'{name},protofast')}]) {{\n"
        "  try { host.loadConfig({ CLIENTS: clients }); } catch { refused.push(clients); }\n"
        "}\n"
        "console.log(JSON.stringify(refused));"
    )
    if refused is None:
        return
    for clients in (f"{PLATFORM},{name}", f"{name},protofast"):
        if clients not in json.loads(refused):
            fail(f"clients/host would load the app console {name} in one process: CLIENTS={clients}")


def check_csp(name):
    """The host sets the admin CSP on the console's responses, and it pins connect-src."""
    answer = ask_host(
        f"console.log(JSON.stringify({{ csp: host.ADMIN_CSP, applies: host.isConsole({json.dumps(name)}) }}));"
    )
    if answer is None:
        return
    answer = json.loads(answer)
    if not answer["applies"]:
        fail(f"clients/host does not set the admin CSP on {name}'s responses")
    if not re.search(r"(^|;\s*)connect-src 'self'\s*(;|$)", answer["csp"]):
        fail("clients/host's ADMIN_CSP no longer pins connect-src to 'self'")


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    project = (ROOT / sys.argv[1]).resolve()
    name = project.name
    if name != PLATFORM and not name.startswith("admin-"):
        raise SystemExit(f"{sys.argv[1]} is not an admin console")
    app = None if name == PLATFORM else name.removeprefix("admin-")

    check_generated(project, app)
    check_kit_link(project)
    check_imports(project)
    check_dependencies(project)
    check_shared_host(name)
    check_csp(name)

    for message in failures:
        print(f"FAIL: {message}")
    if failures:
        return 1
    print(f"{name}: isolation checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
