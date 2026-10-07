#!/usr/bin/env python3
"""Isolation checks for an admin console project (docs/design/admin-consoles.md §5, §7).

    scripts/check-admin-console.py clients/admin-theplot

Run in the console's pipeline after `npm run generate:grpc`. Fails when:

* its generated code holds protos other than Admin/Shared and its own Admin/<App>;
* its src/admin-kit is anything but the symlink to the platform's kit;
* its sources import a package outside the allowed set, or its package.json adds a
  dependency the platform console (clients/admin) does not have;
* the shared clients host is configured to load an app console;
* the admin vhost's CSP no longer pins connect-src to the console's own origin.
"""

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PLATFORM = "admin"
ALLOWED_IMPORTS = re.compile(
    r"^(\.|@angular/|@connectrpc/|@bufbuild/|@opentelemetry/|rxjs|tslib$|express$|node:)"
)
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
        extra = sorted(set(console.get(section, {})) - set(platform.get(section, {})))
        if extra:
            fail(f"{project.name} adds {section} the platform console lacks: {', '.join(extra)}")


def check_shared_host():
    compose = (ROOT / "deploy" / "docker-compose.host-edge.yml").read_text()
    seeds = (ROOT / "infra" / "compute.tf").read_text()
    for text, where in ((compose, "deploy/docker-compose.host-edge.yml"), (seeds, "infra/compute.tf")):
        for clients in re.findall(r'CLIENTS:\s*\$\{CLIENTS:-([^}]*)\}|clients\s*=\s*"([^"]*)"', text):
            for name in ",".join(clients).split(","):
                if name.strip().startswith("admin-"):
                    fail(f"{where}: the shared clients host must not load the app console {name.strip()}")


def check_csp():
    csp = (ROOT / "proxy" / "envoy.admin-csp.yaml.tmpl").read_text()
    if "connect-src 'self'" not in csp:
        fail("proxy/envoy.admin-csp.yaml.tmpl no longer pins connect-src to 'self'")


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
    check_shared_host()
    check_csp()

    for message in failures:
        print(f"FAIL: {message}")
    if failures:
        return 1
    print(f"{name}: isolation checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
