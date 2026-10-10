"""Reads the result files of a test run and writes the per-category tables of docs/test-report.md.

    python3 scripts/test-report/summarize-results.py <unit.trx> <integration.trx> <playwright.json> <tables.md>

The TRX files come from `dotnet test --logger trx`, the JSON from Playwright's json reporter. Prints the totals
(counters, start and finish times, skipped and failed tests) as JSON.
"""
import json
import re
import sys
import xml.etree.ElementTree as ET
from collections import OrderedDict

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def seconds(text):
    h, m, s = text.split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)


def read_trx(path, suite):
    root = ET.parse(path).getroot()
    classes = {}
    for test in root.iterfind(".//t:TestDefinitions/t:UnitTest", NS):
        method = test.find("t:TestMethod", NS)
        classes[test.get("id")] = (method.get("className").split(".")[-1], method.get("name"))
    tests = OrderedDict()
    for result in root.iterfind(".//t:Results/t:UnitTestResult", NS):
        cls, method = classes[result.get("testId")]
        key = (suite, cls, method)
        entry = tests.setdefault(key, {"cases": 0, "passed": 0, "failed": 0, "skipped": 0, "seconds": 0.0})
        entry["cases"] += 1
        outcome = result.get("outcome")
        entry["passed" if outcome == "Passed" else "failed" if outcome == "Failed" else "skipped"] += 1
        entry["seconds"] += seconds(result.get("duration", "00:00:00"))
    counters = root.find(".//t:ResultSummary/t:Counters", NS).attrib
    times = root.find("t:Times", NS).attrib
    return tests, counters, times


def read_playwright(path):
    data = json.load(open(path))
    tests = OrderedDict()

    def walk(suite, file, titles):
        for spec in suite.get("specs", []):
            name = " › ".join(titles + [spec["title"]])
            entry = tests.setdefault(("e2e", file, name), {"cases": 0, "passed": 0, "failed": 0, "skipped": 0, "seconds": 0.0})
            for test in spec["tests"]:
                entry["cases"] += 1
                last = test["results"][-1] if test["results"] else {"status": "skipped", "duration": 0}
                status = test.get("status")
                entry["passed" if status == "expected" else "skipped" if status == "skipped" else "failed"] += 1
                entry["seconds"] += sum(r["duration"] for r in test["results"]) / 1000
        for child in suite.get("suites", []):
            walk(child, file, titles + [child["title"]])

    for suite in data["suites"]:
        walk(suite, suite["title"], [])
    return tests, data["stats"]


unit, unit_counters, unit_times = read_trx(sys.argv[1], "unit")
integration, int_counters, int_times = read_trx(sys.argv[2], "integration")
e2e, e2e_stats = read_playwright(sys.argv[3])
everything = OrderedDict(list(unit.items()) + list(integration.items()) + list(e2e.items()))

ROLLBACK = re.compile(r"audit_record_fails|rolled_back|cannot_be_recorded|Rolling_back|can_be_rolled_back|failing_notifier")
CONCURRENCY = re.compile(r"409|conflict|stale|simultaneous|racing|at_once|same moment|outdated version|changed meanwhile|another screen saves|rowversion_concurrency", re.IGNORECASE)
CONSTRAINT = re.compile(r"unique|_taken_|code that is taken|Simultaneous_creates_with_the_same_code")
NOT_CONCURRENCY = ("SessionSecurityTests", "InventoryHubTests")
AUDIT = re.compile(r"audit", re.IGNORECASE)

categories = OrderedDict(
    [
        ("Veritabanı kısıtları", lambda s, c, m: c in ("DatabaseConstraintTests", "SoftDeleteTests") or bool(CONSTRAINT.search(m))),
        ("Eşzamanlılık", lambda s, c, m: c not in NOT_CONCURRENCY and (c == "ConcurrencyTests" or bool(CONCURRENCY.search(m)) or (s == "e2e" and "concurrency" in c))),
        ("Geri alma (rollback)", lambda s, c, m: bool(ROLLBACK.search(m))),
        (
            "Denetim kaydı (audit)",
            lambda s, c, m: not ROLLBACK.search(m)
            and c != "ModelConfigurationTests"
            and (c in ("AuditLogApiTests", "AuditableEntityInterceptorTests", "AssetAuditTrailTests", "AuditLogRequestTests") or bool(AUDIT.search(m)) or (s == "e2e" and "audit" in c)),
        ),
    ]
)

SUITE = {"unit": "Birim", "integration": "SQL Server entegrasyon", "e2e": "Tarayıcı (E2E)"}


def outcome(entry):
    if entry["failed"]:
        return f"**{entry['failed']} başarısız**"
    if entry["skipped"] == entry["cases"]:
        return "atlandı"
    text = "geçti"
    if entry["cases"] > 1:
        text += f" ({entry['passed']}/{entry['cases']} durum)"
    return text


def readable(method):
    return method.replace("_", " ")


out = []
for title, rule in categories.items():
    order = {"unit": 0, "integration": 1, "e2e": 2}
    rows = sorted(
        ((k, v) for k, v in everything.items() if rule(*k) and k[2] not in ("InitializeAsync", "Dispose")),
        key=lambda row: (order[row[0][0]], row[0][1], row[0][2]),
    )
    passed = sum(1 for _, v in rows if not v["failed"] and v["passed"])
    out.append(f"### {title}\n")
    out.append(f"{len(rows)} test, {passed} geçti, {sum(1 for _, v in rows if v['failed'])} başarısız.\n")
    out.append("| Takım | Sınıf / dosya | Test | Sonuç | Süre (sn) |")
    out.append("| --- | --- | --- | --- | ---: |")
    for (suite, cls, method), v in rows:
        out.append(f"| {SUITE[suite]} | `{cls}` | {readable(method)} | {outcome(v)} | {v['seconds']:.2f} |")
    out.append("")

summary = {
    "unit": unit_counters,
    "unit_times": unit_times,
    "integration": int_counters,
    "integration_times": int_times,
    "e2e": e2e_stats,
    "e2e_tests": len(e2e),
    "unit_methods": len(unit),
    "integration_methods": len(integration),
    "failed": [f"{k[0]} {k[1]}.{k[2]}" for k, v in everything.items() if v["failed"]],
    "skipped": [f"{k[0]} {k[1]}.{k[2]}" for k, v in everything.items() if v["skipped"]],
    "classes": sorted({f"{k[0]}:{k[1]}" for k in everything}),
}
open(sys.argv[4], "w").write("\n".join(out))
print(json.dumps(summary, indent=1, ensure_ascii=False))
