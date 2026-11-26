#!/usr/bin/env python3
"""Exercise the real SinalFrio stack through its loopback frontend.

Start a dedicated Compose project with a fresh database before running this.
No data is deleted: use a new disposable database for each run. --resilience
stops and restarts RabbitMQ in the verified project only.
"""

import argparse
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager, nullcontext
from datetime import datetime, timedelta, timezone
import json
import math
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener
from uuid import uuid4

ROOT = Path(__file__).resolve().parents[1]
SHIPMENT_ID = "00000000-0000-4000-8000-000000000001"
SHIPMENT = f"/api/v1/shipments/{SHIPMENT_ID}"
OTHER_SHIPMENT = "/api/v1/shipments/00000000-0000-4000-8000-000000000002"
SERVICES = {"db", "rabbitmq", "worker", "api", "frontend"}
SENSOR_KEY = "SINALFRIO_SENSOR_TOKEN"
OPERATOR_KEY = "SINALFRIO_OPERATOR_TOKEN"
NOTE = "Demo: excursão confirmada após leitura atrasada."


class DemoError(Exception):
    pass


def require(condition, message):
    if not condition:
        raise DemoError(message)


def settings():
    # Not shell-sourced: bootstrap uses plain KEY=value lines.
    path = ROOT / ".env"
    require(path.is_file(), "Create the repository-root .env before running the demo.")
    require(path.stat().st_mode & 0o777 == 0o600, "Set repository-root .env mode to 0600.")
    values = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        key, sep, value = line.partition("=")
        if sep and key.strip() in {SENSOR_KEY, OPERATOR_KEY, "SINALFRIO_PORT"}:
            value = value.strip()
            if value and value[0] in "\"'" and value[-1:] == value[0]:
                value = value[1:-1]
            values[key.strip()] = value
    for key in (SENSOR_KEY, OPERATOR_KEY):
        require(re.fullmatch(r"[\x21-\x7e]+", values.get(key, "")), f"Missing or invalid {key} in .env.")
    require(values[SENSOR_KEY] != values[OPERATOR_KEY], "Sensor and operator credentials must differ.")
    port = os.environ.get("SINALFRIO_PORT") or values.get("SINALFRIO_PORT") or "5186"
    require(re.fullmatch(r"[0-9]{1,5}", port) and 1 <= int(port) <= 65535, "Invalid SINALFRIO_PORT.")
    return int(port), values[SENSOR_KEY], values[OPERATOR_KEY]


def run_docker(args, description, timeout=30):
    # Compose can print interpolated values to stderr on errors; never echo output.
    try:
        result = subprocess.run(["docker", *args], cwd=ROOT, capture_output=True,
                                text=True, timeout=timeout, check=False)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise DemoError(f"{description} failed ({type(exc).__name__}).") from None
    require(result.returncode == 0, f"{description} failed (exit {result.returncode}).")
    return result.stdout


def compose(project, *args, timeout=30):
    scope = ["--project-name", project] if project else []
    return run_docker(["compose", "--project-directory", str(ROOT), *scope, *args],
                      f"Compose {args[0]}", timeout)


def compose_ps(project, *services):
    output = compose(project, "ps", "--all", "--format", "json", *services).strip()
    if not output:
        return []
    if output.startswith("["):
        return json.loads(output)
    return [json.loads(line) for line in output.splitlines()]


def local_project(project, port):
    inventory = compose_ps(project)
    migrations = [row for row in inventory if row.get("Service") == "migrate"]
    require(len(migrations) == 1 and migrations[0].get("State") == "exited"
            and migrations[0].get("ExitCode") == 0, "Expected one successful migration container.")
    rows = [row for row in inventory if row.get("Service") != "migrate"]
    require(len(rows) == len(SERVICES) and {r.get("Service") for r in rows} == SERVICES,
            "Expected exactly one container for each SinalFrio service in the dedicated project.")
    projects = {r.get("Project") for r in rows}
    require(len(projects) == 1, "Compose returned mixed projects.")
    actual = projects.pop()
    require(isinstance(actual, str) and re.fullmatch(r"[a-z0-9][a-z0-9_-]*", actual),
            "Could not determine the Compose project name.")
    require(project is None or project == actual, "Compose project name did not match.")
    require(all(r.get("State") == "running" for r in rows), "Start all SinalFrio services first.")
    ids = [r.get("ID", "") for r in rows]
    require(all(re.fullmatch(r"[a-f0-9]{12,64}", i) for i in ids), "Invalid Compose container identifiers.")
    # Inspect only one label, never the container config/environment.
    label = '{{index .Config.Labels "com.docker.compose.project.working_dir"}}'
    directories = run_docker(["inspect", "--format", label, *ids], "Compose ownership check").splitlines()
    require(len(directories) == len(rows) and all(Path(d).resolve() == ROOT for d in directories),
            "Refusing to use containers created from another project directory.")
    front = next(r for r in rows if r["Service"] == "frontend")
    require(any(p.get("URL") == "127.0.0.1" and p.get("PublishedPort") == port
                and p.get("Protocol") == "tcp" for p in (front.get("Publishers") or [])),
            "The selected project's frontend does not publish the requested loopback port.")
    return actual, next(r["ID"] for r in rows if r["Service"] == "rabbitmq")


@contextmanager
def broker_outage(project, broker_id):
    def broker_state():
        rows = compose_ps(project, "rabbitmq")
        require(len(rows) == 1 and rows[0].get("ID") == broker_id
                and rows[0].get("Project") == project, "RabbitMQ container identity changed.")
        return rows[0].get("State")

    require(broker_state() == "running", "RabbitMQ must be running before the outage test.")
    try:
        compose(project, "stop", "rabbitmq", timeout=60)
        require(broker_state() == "exited", "RabbitMQ did not stop.")
        print("RabbitMQ stopped in the verified Compose project.", flush=True)
        yield
    finally:
        compose(project, "start", "rabbitmq", timeout=60)
        require(broker_state() == "running", "RabbitMQ did not restart.")
        print("RabbitMQ restarted.", flush=True)


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


class Api:
    def __init__(self, port, operator):
        self.origin = f"http://127.0.0.1:{port}"
        self.operator = operator
        self.opener = build_opener(ProxyHandler({}), NoRedirect())

    def request(self, label, method, path, token, expected, payload=None):
        headers = {"Accept": "application/json"}
        if token is not None:
            headers["Authorization"] = f"Bearer {token}"
        data = None
        if payload is not None:
            headers["Content-Type"] = "application/json"
            data = json.dumps(payload).encode()
        req = Request(self.origin + path, data, headers, method=method)
        try:
            response = self.opener.open(req, timeout=5)
        except HTTPError as exc:
            response = exc
        except (URLError, OSError) as exc:
            raise DemoError(f"{label}: connection failed ({type(exc).__name__}).") from None
        with response:
            require(response.code in expected, f"{label}: expected HTTP {expected}, got {response.code}.")
            if response.code == 200:
                return json.load(response)
        return None

    def detail(self):
        detail = self.request("Shipment detail", "GET", SHIPMENT, self.operator, (200,))
        if not isinstance(detail, dict) or detail.get("id") != SHIPMENT_ID or detail.get("code") != "SF-001":
            raise DemoError("Shipment detail did not identify SF-001.")
        return detail


def wait_evaluated(api, version, status, timeout):
    deadline = time.monotonic() + timeout
    while True:
        detail = api.detail()
        require(detail["readingVersion"] == version, f"Unexpected reading version while waiting for version {version}.")
        if detail["evaluatedVersion"] == version:
            require(detail["assessmentStatus"] == status, f"Version {version} did not evaluate to {status}.")
            return detail
        remaining = deadline - time.monotonic()
        require(remaining > 0, f"Timed out waiting for evaluated version {version}.")
        time.sleep(min(0.5, remaining))


def scenario(api, sensor, timeout, *, broker=None):
    first = api.detail()
    require(first["readingVersion"] == first["evaluatedVersion"] == 0 and first["readings"] == []
            and first["review"] is None and first["excursion"] is None,
            "SF-001 is not pristine. Use a fresh isolated Compose database; no records were changed.")
    anchor = datetime.now(timezone.utc).replace(microsecond=0) - timedelta(minutes=20)

    def reading(minutes, celsius):
        observed = anchor + timedelta(minutes=minutes)
        return {"id": str(uuid4()), "observedAt": observed.isoformat().replace("+00:00", "Z"), "celsius": celsius}

    initial = [reading(0, 11), reading(10, 11), reading(13, 7)]
    late = reading(5, 12)
    readings_path = SHIPMENT + "/readings"
    reviews_path = SHIPMENT + "/reviews"

    def ingest(label, payload, expected=202, token=sensor):
        api.request(label, "POST", readings_path, token, (expected,), payload)

    def review(label, version, expected, token=api.operator):
        return api.request(label, "POST", reviews_path, token, expected,
                           {"expectedVersion": version, "decision": "acknowledged", "note": NOTE})

    api.request("Unauthenticated read", "GET", SHIPMENT, None, (401,))
    api.request("Sensor cannot read operator data", "GET", SHIPMENT, sensor, (401, 403))
    api.request("Operator cannot ingest sensor data", "POST", readings_path, api.operator, (401, 403), initial[0])
    api.request("Sensor cannot write another shipment", "POST", OTHER_SHIPMENT + "/readings", sensor, (403,), initial[0])
    ingest("Reject a supplied organization", {**initial[0], "organizationId": str(uuid4())}, expected=400)
    ingest("Require a timestamp with offset", {**initial[0], "observedAt": "2026-01-01T12:00:00"}, expected=400)
    review("Sensor cannot review", 0, (401, 403), token=sensor)
    for payload in initial:
        ingest("Ingest initial reading", payload)
    baseline = wait_evaluated(api, 3, "insufficientEvidence", timeout)
    require(baseline["excursion"] is None, "Sparse initial readings must not prove an excursion.")
    review("Cannot review without excursion", 3, (409,))
    print("Three readings evaluated: insufficient evidence.", flush=True)
    with (broker_outage(*broker) if broker else nullcontext()):
        with ThreadPoolExecutor(max_workers=2) as pool:
            attempts = [pool.submit(ingest, "Concurrent identical late reading", late) for _ in range(2)]
            for attempt in attempts:
                attempt.result()
        ingest("Conflicting reading ID", {**late, "celsius": 13}, expected=409)
        current = api.detail()
        require(current["readingVersion"] == 4, "Late reading, exact retry and conflict must add exactly one version.")
        if broker:
            require(current["evaluatedVersion"] == 3 and current["assessmentStatus"] == "pending",
                    "Broker outage must leave the durably accepted reading unevaluated.")
            review("Cannot review pending assessment", 4, (409,))
            print("Late reading accepted while offline; assessment remains pending.", flush=True)

    assessed = wait_evaluated(api, 4, "excursion", timeout)
    require(isinstance(assessed["excursion"], dict) and assessed["review"] is None,
            "Expected an unreviewed excursion at version 4.")

    def instant(value):
        return datetime.fromisoformat(value.replace("Z", "+00:00"))

    observed = sorted(assessed["readings"], key=lambda row: instant(row["observedAt"]))
    expected_readings = [initial[0], late, *initial[1:]]
    require([(r["id"], instant(r["observedAt"]), r["celsius"]) for r in observed]
            == [(r["id"], instant(r["observedAt"]), r["celsius"]) for r in expected_readings],
            "Reading detail must retain all four original observations, with 7 °C latest by observation time.")
    require(instant(assessed["excursion"]["startedAt"]) == anchor,
            "Excursion start must reflect the earliest observation, not the late arrival.")
    review("Stale review", 3, (409,))
    with ThreadPoolExecutor(max_workers=2) as pool:
        attempts = [pool.submit(review, "Concurrent acknowledgement", 4, (200, 409)) for _ in range(2)]
        written = [result for attempt in attempts if (result := attempt.result()) is not None]
    require(len(written) == 1, "Exactly one concurrent review must be accepted.")
    saved = written[0]
    review("Duplicate review", 4, (409,))
    recorded = api.detail()
    acknowledgement = recorded["review"]
    require(isinstance(saved, dict) and isinstance(acknowledgement, dict) and saved["review"] == acknowledgement
            and acknowledgement["decision"] == "acknowledged" and acknowledgement["evaluatedVersion"] == 4
            and acknowledgement["note"] == NOTE and acknowledgement["reviewedAt"]
            and recorded["excursion"] == assessed["excursion"]
            and recorded["readingVersion"] == recorded["evaluatedVersion"] == 4,
            "Review must persist once without changing the excursion or versions.")
    print("PASS: late reading established an excursion; duplicate ingest and review were handled correctly.")


def verify_republication(project, api, timeout):
    """Recover a confirmed message whose database marker was not saved, on QA only."""
    def sql(statement):
        return compose(project, "exec", "-T", "db", "psql", "-XAtq", "-v", "ON_ERROR_STOP=1",
                       "-U", "postgres", "-d", "sinalfrio", "-c", statement).strip()

    def until(check):
        deadline = time.monotonic() + timeout
        while not check():
            require(time.monotonic() < deadline, "Timed out verifying delivery of the same outbox message.")
            time.sleep(0.5)

    before = api.detail()
    privileges = sql("""
        SELECT has_schema_privilege('sinalfrio_runtime', 'public', 'CREATE'),
            has_table_privilege('sinalfrio_runtime', 'public.sf_reviews', 'INSERT'),
            has_any_column_privilege('sinalfrio_runtime', 'public.sf_reviews', 'UPDATE'),
            has_table_privilege('sinalfrio_runtime', 'public.sf_reviews', 'DELETE');
        """)
    require(privileges == "f|t|f|f", "Runtime role must append reviews without rewrite or DDL access.")
    message_id = sql("SELECT id FROM public.sf_outbox WHERE payload->>'readingVersion' = '4';")
    require(re.fullmatch(r"[0-9a-f-]{36}", message_id), "Expected one outbox row for version 4.")

    def settlements():
        logs = compose(project, "logs", "--no-color", "worker")
        return logs.count(f"Settled evaluation {message_id}")

    settled = f"FROM public.sf_outbox WHERE id = '{message_id}' AND published_at IS NOT NULL"
    until(lambda: sql(f"SELECT count(*) {settled};") == "1" and settlements() == 1)
    previous = sql(f"SELECT published_at {settled};")
    sql(f"UPDATE public.sf_outbox SET published_at = NULL WHERE id = '{message_id}';")
    until(lambda: settlements() >= 2 and sql(f"SELECT published_at {settled};") not in ("", previous))
    require(sql(f"SELECT count(*) FROM public.sf_inbox WHERE message_id = '{message_id}';") == "1",
            "Reprocessing changed inbox cardinality.")
    require(api.detail() == before, "Reprocessing changed the assessment or the recorded review.")
    print("PASS: RabbitMQ delivered the same message twice; one inbox row and the original review remained.")


def terminate(_signum, _frame):
    raise KeyboardInterrupt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("-p", "--project-name", help="Exact name passed to docker compose -p (default: Compose resolution).")
    parser.add_argument("--resilience", action="store_true", help="Stop and restore RabbitMQ in the verified isolated project.")
    parser.add_argument("--verify-republication", action="store_true", help="Simulate a lost outbox marker in an explicitly named QA/CI project.")
    parser.add_argument("--timeout", type=float, default=90, help="Seconds to wait per evaluation (default: 90).")
    args = parser.parse_args()
    if not math.isfinite(args.timeout) or args.timeout <= 0:
        parser.error("--timeout must be a finite positive number")
    if args.project_name is not None and not re.fullmatch(r"[a-z0-9][a-z0-9_-]*", args.project_name):
        parser.error("--project-name is not a valid Compose project name")
    if args.verify_republication and args.project_name not in {"sinalfrio-qa", "sinalfrio-ci"}:
        parser.error("--verify-republication requires the isolated sinalfrio-qa or sinalfrio-ci project")
    signal.signal(signal.SIGTERM, terminate)
    try:
        port, sensor, operator = settings()
        project, broker_id = local_project(args.project_name, port)
        scenario(Api(port, operator), sensor, args.timeout,
                 broker=(project, broker_id) if args.resilience else None)
        if args.verify_republication:
            verify_republication(project, Api(port, operator), args.timeout)
    except DemoError as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        return 1
    except (ValueError, TypeError, KeyError, AttributeError, OSError) as exc:
        print(f"FAIL: invalid or unavailable Docker/API response ({type(exc).__name__}).", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("Interrupted.", file=sys.stderr)
        return 130
    return 0


if __name__ == "__main__":
    sys.exit(main())
