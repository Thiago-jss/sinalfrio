#!/usr/bin/env python3
"""Generate private, disposable credentials without overwriting existing ones."""

import os
import re
import secrets
import stat
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SECRETS = (
    "SINALFRIO_DB_ADMIN_PASSWORD",
    "SINALFRIO_DB_MIGRATOR_PASSWORD",
    "SINALFRIO_DB_RUNTIME_PASSWORD",
    "SINALFRIO_RABBIT_PASSWORD",
    "SINALFRIO_SENSOR_TOKEN",
    "SINALFRIO_OPERATOR_TOKEN",
)
PORT = "SINALFRIO_PORT"


def validate_file(path: Path) -> None:
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_mode & 0o077:
        raise SystemExit("O arquivo .env precisa ser um arquivo regular privado (chmod 600).")
    values = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        key, sep, value = line.partition("=")
        if not sep or key in values:
            raise SystemExit("Entrada inválida ou repetida no .env.")
        values[key] = value
    tokens = [values.get(key, "") for key in SECRETS]
    if any(not re.fullmatch(r"[a-f0-9]{48}", token) for token in tokens) or len(set(tokens)) != len(tokens):
        raise SystemExit("O .env existente não contém todas as credenciais locais distintas e válidas.")
    port = values.get(PORT, "")
    if not port.isascii() or not port.isdecimal() or not 1024 <= int(port) <= 65535:
        raise SystemExit("Configure SINALFRIO_PORT entre 1024 e 65535 no .env.")


def main() -> None:
    path = ROOT / ".env"
    try:
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    except FileExistsError:
        validate_file(path)
        print("Configuração .env existente preservada.")
        return
    with os.fdopen(fd, "w", encoding="utf-8") as handle:
        for key in SECRETS:
            handle.write(f"{key}={secrets.token_hex(24)}\n")
        handle.write(f"{PORT}=5186\n")
    validate_file(path)
    print("Configuração .env gerada com permissões privadas.")


if __name__ == "__main__":
    main()
