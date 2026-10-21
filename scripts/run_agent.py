#!/usr/bin/env python3
"""Start the local agent with settings from an ignored .env file."""

from __future__ import annotations

import os
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ENVIRONMENTS = {"development": "Development", "demo": "Demo", "production": "Production"}
REQUIRED = (
    "ConnectionStrings__Postgres",
    "AgentApi__Key",
    "GitHub__Repository",
    "GitHub__Branch",
)


def load_environment(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for number, raw_line in enumerate(path.read_text().splitlines(), start=1):
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        key, separator, value = line.partition("=")
        key = key.strip()
        if not separator or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", key):
            raise ValueError(f"Invalid setting at {path.name}:{number}")
        value = value.strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in {'"', "'"}:
            value = value[1:-1]
        values[key] = value
    return values


def main() -> None:
    if len(sys.argv) != 2 or sys.argv[1] not in ENVIRONMENTS:
        raise SystemExit("Usage: python3 scripts/run_agent.py development|demo|production")

    environment = sys.argv[1]
    path = ROOT / f".env.{environment}"
    if not path.exists():
        raise SystemExit(f"Missing {path.name}; copy .env.{environment}.example and fill it.")

    settings = load_environment(path)
    for key, value in settings.items():
        os.environ.setdefault(key, value)
    os.environ["ASPNETCORE_ENVIRONMENT"] = ENVIRONMENTS[environment]

    missing = [key for key in REQUIRED if not os.environ.get(key)]
    if missing:
        raise SystemExit("Missing required settings: " + ", ".join(missing))

    os.chdir(ROOT)
    os.execvp("dotnet", ["dotnet", "run", "--project", "src/QaAgent.Host"])


if __name__ == "__main__":
    main()
