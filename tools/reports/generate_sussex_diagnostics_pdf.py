#!/usr/bin/env python3
"""Generate a shareable Sussex LSL/twin diagnostics PDF from report JSON."""

from __future__ import annotations

import argparse
import json
import textwrap
from pathlib import Path
from typing import Any, Iterable

import matplotlib.pyplot as plt
from matplotlib.backends.backend_pdf import PdfPages


PAGE_W = 8.27
PAGE_H = 11.69
MARGIN_X = 0.55
TOP_Y = 10.95
BOTTOM_Y = 0.55
BODY_SIZE = 8.0
SMALL_SIZE = 7.0
TITLE_COLOR = "#1f2933"
TEXT_COLOR = "#263238"
MUTED_COLOR = "#5c6770"
RULE_COLOR = "#c7d0d9"
LEVEL_COLORS = {
    "Success": "#1f7a4c",
    "Warning": "#9a6a00",
    "Failure": "#b3261e",
    "Preview": "#5b6470",
    "OK": "#1f7a4c",
    "WARN": "#9a6a00",
    "FAIL": "#b3261e",
    "PREVIEW": "#5b6470",
}


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8-sig") as handle:
        return json.load(handle)


def text_value(value: Any) -> str:
    if value is None:
        return "n/a"
    if isinstance(value, bool):
        return "yes" if value else "no"
    return str(value)


def wrap_lines(text: Any, width: int = 118) -> list[str]:
    raw = text_value(text).replace("\r", "")
    lines: list[str] = []
    for paragraph in raw.split("\n"):
        if not paragraph.strip():
            lines.append("")
            continue
        lines.extend(textwrap.wrap(paragraph, width=width, break_long_words=False, replace_whitespace=False) or [""])
    return lines


def level_label(level: Any) -> str:
    raw = text_value(level)
    return {
        "Success": "OK",
        "Warning": "WARN",
        "Failure": "FAIL",
        "Preview": "PREVIEW",
    }.get(raw, raw.upper())


class PdfWriter:
    def __init__(self, pdf: PdfPages, report: dict[str, Any]):
        self.pdf = pdf
        self.report = report
        self.fig = None
        self.ax = None
        self.y = TOP_Y
        self.page = 0

    def new_page(self) -> None:
        if self.fig is not None:
            self.ax.text(PAGE_W - MARGIN_X, 0.28, f"Page {self.page}", ha="right", va="bottom", fontsize=6, color=MUTED_COLOR)
            self.pdf.savefig(self.fig, bbox_inches="tight")
            plt.close(self.fig)

        self.page += 1
        self.fig, self.ax = plt.subplots(figsize=(PAGE_W, PAGE_H))
        self.ax.axis("off")
        self.y = TOP_Y

    def finish(self) -> None:
        if self.fig is None:
            return
        self.ax.text(PAGE_W - MARGIN_X, 0.28, f"Page {self.page}", ha="right", va="bottom", fontsize=6, color=MUTED_COLOR)
        self.pdf.savefig(self.fig, bbox_inches="tight")
        plt.close(self.fig)
        self.fig = None
        self.ax = None

    def ensure(self, needed: float) -> None:
        if self.fig is None or self.y - needed < BOTTOM_Y:
            self.new_page()

    def title(self, title: str, subtitle: str) -> None:
        self.ensure(0.9)
        self.ax.text(MARGIN_X, self.y, title, ha="left", va="top", fontsize=15, weight="bold", color=TITLE_COLOR)
        self.y -= 0.32
        self.ax.text(MARGIN_X, self.y, subtitle, ha="left", va="top", fontsize=8.5, color=MUTED_COLOR)
        self.y -= 0.32
        self.ax.plot([MARGIN_X, PAGE_W - MARGIN_X], [self.y, self.y], color=RULE_COLOR, linewidth=0.8)
        self.y -= 0.28

    def section(self, title: str) -> None:
        self.ensure(0.55)
        self.y -= 0.06
        self.ax.text(MARGIN_X, self.y, title, ha="left", va="top", fontsize=10.5, weight="bold", color=TITLE_COLOR)
        self.y -= 0.23
        self.ax.plot([MARGIN_X, PAGE_W - MARGIN_X], [self.y, self.y], color=RULE_COLOR, linewidth=0.5)
        self.y -= 0.18

    def key_values(self, rows: Iterable[tuple[str, Any]]) -> None:
        for key, value in rows:
            lines = wrap_lines(value, width=88)
            needed = max(0.22, 0.17 * len(lines)) + 0.05
            self.ensure(needed)
            self.ax.text(MARGIN_X, self.y, key, ha="left", va="top", fontsize=SMALL_SIZE, weight="bold", color=MUTED_COLOR)
            x_value = MARGIN_X + 1.85
            for i, line in enumerate(lines):
                self.ax.text(x_value, self.y - 0.17 * i, line, ha="left", va="top", fontsize=SMALL_SIZE, color=TEXT_COLOR)
            self.y -= needed

    def check_rows(self, checks: Iterable[dict[str, Any]]) -> None:
        for check in checks:
            level = level_label(check.get("Level"))
            label = text_value(check.get("Label"))
            body = f"{text_value(check.get('Summary'))} {text_value(check.get('Detail'))}".strip()
            lines = wrap_lines(body, width=82)
            needed = max(0.26, 0.17 * len(lines)) + 0.08
            self.ensure(needed)
            self.ax.text(MARGIN_X, self.y, level, ha="left", va="top", fontsize=SMALL_SIZE, weight="bold", color=LEVEL_COLORS.get(level, MUTED_COLOR))
            self.ax.text(MARGIN_X + 0.72, self.y, label, ha="left", va="top", fontsize=SMALL_SIZE, weight="bold", color=TEXT_COLOR)
            x_body = MARGIN_X + 2.55
            for i, line in enumerate(lines):
                self.ax.text(x_body, self.y - 0.17 * i, line, ha="left", va="top", fontsize=SMALL_SIZE, color=TEXT_COLOR)
            self.y -= needed

    def telemetry_rows(self, rows: Iterable[dict[str, Any]]) -> None:
        for row in rows:
            key = text_value(row.get("Key"))
            value = text_value(row.get("Value"))
            lines = wrap_lines(value, width=86)
            needed = max(0.2, 0.16 * len(lines)) + 0.04
            self.ensure(needed)
            self.ax.text(MARGIN_X, self.y, key, ha="left", va="top", fontsize=6.5, color=MUTED_COLOR, family="monospace")
            x_value = MARGIN_X + 2.65
            for i, line in enumerate(lines):
                self.ax.text(x_value, self.y - 0.16 * i, line, ha="left", va="top", fontsize=6.5, color=TEXT_COLOR, family="monospace")
            self.y -= needed


def build_pdf(report: dict[str, Any], output_pdf: Path) -> None:
    output_pdf.parent.mkdir(parents=True, exist_ok=True)
    with PdfPages(output_pdf) as pdf:
        writer = PdfWriter(pdf, report)
        writer.new_page()
        writer.title(
            f"{text_value(report.get('StudyLabel'))} LSL/Twin Diagnostics",
            f"Generated {text_value(report.get('GeneratedAtUtc'))} | schema {text_value(report.get('SchemaVersion'))}",
        )
        overall = level_label(report.get("Level"))
        writer.section("Summary")
        writer.key_values([
            ("overall", f"{overall} - {text_value(report.get('Summary'))}"),
            ("detail", report.get("Detail")),
            ("study", report.get("StudyId")),
            ("package", report.get("PackageId")),
            ("expected upstream", f"{text_value(report.get('ExpectedLslStreamName'))} / {text_value(report.get('ExpectedLslStreamType'))}"),
            ("report folder", report.get("ReportDirectory")),
        ])

        setup = report.get("QuestSetup", {})
        writer.section("Quest Setup")
        writer.key_values([
            ("selector", setup.get("Selector")),
            ("foreground", setup.get("ForegroundAndSnapshot")),
            ("pinned build", setup.get("PinnedBuild")),
            ("device profile", setup.get("DeviceProfileSummary")),
        ])

        writer.section("Windows Environment")
        windows_env = report.get("WindowsEnvironment", {})
        writer.key_values([
            ("summary", f"{level_label(windows_env.get('Level'))} - {text_value(windows_env.get('Summary'))}"),
            ("detail", windows_env.get("Detail")),
        ])
        writer.check_rows(windows_env.get("Checks", []))

        writer.section("Machine LSL State")
        machine = report.get("MachineLslState", {})
        writer.key_values([
            ("summary", f"{level_label(machine.get('Level'))} - {text_value(machine.get('Summary'))}"),
            ("detail", machine.get("Detail")),
        ])
        writer.check_rows(machine.get("Checks", []))

        writer.section("Quest Twin Return Path")
        twin_pub = report.get("TwinStatePublisherInventory", {})
        twin = report.get("TwinConnection", {})
        writer.key_values([
            ("publisher", f"{level_label(twin_pub.get('Level'))} - {text_value(twin_pub.get('Summary'))} {text_value(twin_pub.get('Detail'))}"),
            ("connection", f"{level_label(twin.get('Level'))} - {text_value(twin.get('Summary'))}"),
            ("expected inlet", twin.get("ExpectedInlet")),
            ("runtime target", twin.get("RuntimeTarget")),
            ("connected inlet", twin.get("ConnectedInlet")),
            ("counts", twin.get("Counts")),
            ("quest status", twin.get("QuestStatus")),
            ("quest echo", twin.get("QuestEcho")),
            ("return path", twin.get("ReturnPath")),
            ("transport", twin.get("TransportDetail")),
        ])

        writer.section("Command Acceptance")
        command = report.get("CommandAcceptance", {})
        writer.key_values([
            ("summary", f"{level_label(command.get('Level'))} - {text_value(command.get('Summary'))}"),
            ("action id", command.get("ActionId")),
            ("sequence", command.get("Sequence")),
            ("accepted", command.get("Accepted")),
            ("last action id", command.get("LastReportedActionId")),
            ("last action seq", command.get("LastReportedActionSequence")),
            ("last particle seq", command.get("LastReportedParticleSequence")),
            ("detail", command.get("Detail")),
        ])

        telemetry = report.get("TwinTelemetry", [])
        if telemetry:
            writer.section("Key Twin Telemetry")
            writer.telemetry_rows(telemetry)

        artifacts = report.get("Artifacts", [])
        if artifacts:
            writer.section("Artifacts")
            writer.key_values((text_value(row.get("Key")), row.get("Value")) for row in artifacts)

        writer.finish()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-json", required=True, type=Path)
    parser.add_argument("--output-pdf", required=True, type=Path)
    args = parser.parse_args()

    report = read_json(args.input_json)
    build_pdf(report, args.output_pdf)
    print(args.output_pdf)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
