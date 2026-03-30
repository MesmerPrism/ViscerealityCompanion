from __future__ import annotations

import argparse
import csv
import json
import math
import statistics
import sys
import tempfile
from datetime import datetime
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.platypus import Image, Paragraph, SimpleDocTemplate, Spacer


def read_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def read_breathing_trace(path: Path) -> list[tuple[float, float]]:
    rows: list[tuple[float, float]] = []
    with path.open("r", encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            timestamp = row.get("recorded_at_utc", "").strip()
            raw = row.get("breath_volume01", "").strip()
            if not timestamp or not raw:
                continue
            try:
                rows.append((parse_iso_timestamp(timestamp), clamp_unit(float(raw))))
            except ValueError:
                continue
    return rows


def read_signal_trace(path: Path, signal_name: str) -> list[tuple[float, float]]:
    rows: list[tuple[float, float]] = []
    with path.open("r", encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            if row.get("signal_name", "").strip().lower() != signal_name.lower():
                continue
            timestamp = (row.get("source_timestamp_utc") or row.get("recorded_at_utc") or "").strip()
            raw = row.get("value_numeric", "").strip()
            if not timestamp or not raw:
                continue
            try:
                rows.append((parse_iso_timestamp(timestamp), clamp_unit(float(raw))))
            except ValueError:
                continue
    return rows


def read_clock_alignment(path: Path) -> list[tuple[float, float, float]]:
    rows: list[tuple[float, float, float]] = []
    with path.open("r", encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            try:
                sequence = float(row.get("probe_sequence", ""))
                roundtrip = float(row.get("roundtrip_seconds", ""))
                offset = float(row.get("quest_minus_windows_clock_seconds", ""))
            except ValueError:
                continue
            rows.append((sequence, roundtrip * 1000.0, offset * 1000.0))
    return rows


def parse_iso_timestamp(value: str) -> float:
    normalized = value.strip()
    if normalized.endswith("Z"):
        normalized = normalized[:-1] + "+00:00"
    return datetime.fromisoformat(normalized).timestamp()


def clamp_unit(value: float) -> float:
    return max(0.0, min(1.0, value))


def plot_single_trace(points: list[tuple[float, float]], title: str, color: str, output_path: Path) -> str:
    if not points:
        return f"{title}: no samples found."

    start = points[0][0]
    xs = [sample[0] - start for sample in points]
    ys = [sample[1] for sample in points]

    fig, ax = plt.subplots(figsize=(8.4, 2.8), dpi=160)
    fig.patch.set_facecolor("#05070c")
    ax.set_facecolor("#09101a")
    ax.plot(xs, ys, color=color, linewidth=2.2)
    ax.set_xlim(left=0.0)
    ax.set_ylim(0.0, 1.0)
    ax.set_title(title, color="white", fontsize=12, loc="left")
    ax.set_xlabel("Seconds", color="#9fb2d6")
    ax.set_ylabel("Normalized", color="#9fb2d6")
    ax.tick_params(colors="#dce8ff", labelsize=9)
    for spine in ax.spines.values():
        spine.set_color("#1a3c66")
    ax.grid(color="#16304f", alpha=0.55, linewidth=0.8)
    fig.tight_layout()
    fig.savefig(output_path, bbox_inches="tight", facecolor=fig.get_facecolor())
    plt.close(fig)

    return f"{title}: {len(points)} samples across {xs[-1]:.1f}s."


def plot_dual_trace(
    windows_points: list[tuple[float, float]],
    quest_points: list[tuple[float, float]],
    title: str,
    windows_color: str,
    quest_color: str,
    output_path: Path,
) -> str:
    if not windows_points and not quest_points:
        return f"{title}: no samples found."

    starts = [series[0][0] for series in (windows_points, quest_points) if series]
    start = min(starts) if starts else 0.0

    fig, ax = plt.subplots(figsize=(8.4, 2.8), dpi=160)
    fig.patch.set_facecolor("#05070c")
    ax.set_facecolor("#09101a")

    if windows_points:
        xs = [sample[0] - start for sample in windows_points]
        ys = [sample[1] for sample in windows_points]
        ax.plot(xs, ys, color=windows_color, linewidth=2.0, label=f"Windows ({len(windows_points)})")

    if quest_points:
        xs = [sample[0] - start for sample in quest_points]
        ys = [sample[1] for sample in quest_points]
        ax.plot(xs, ys, color=quest_color, linewidth=2.0, label=f"Quest ({len(quest_points)})")

    ax.set_xlim(left=0.0)
    ax.set_ylim(0.0, 1.0)
    ax.set_title(title, color="white", fontsize=12, loc="left")
    ax.set_xlabel("Seconds", color="#9fb2d6")
    ax.set_ylabel("Normalized", color="#9fb2d6")
    ax.tick_params(colors="#dce8ff", labelsize=9)
    for spine in ax.spines.values():
        spine.set_color("#1a3c66")
    ax.grid(color="#16304f", alpha=0.55, linewidth=0.8)
    legend = ax.legend(facecolor="#09101a", edgecolor="#1a3c66", framealpha=1.0, fontsize=9)
    for text in legend.get_texts():
        text.set_color("#dce8ff")
    fig.tight_layout()
    fig.savefig(output_path, bbox_inches="tight", facecolor=fig.get_facecolor())
    plt.close(fig)

    return f"{title}: Windows {len(windows_points)} samples, Quest {len(quest_points)} samples."


def plot_clock_alignment(samples: list[tuple[float, float, float]], output_path: Path) -> str:
    if not samples:
        return "Clock alignment: no probe samples found."

    probe_ids = [row[0] for row in samples]
    roundtrips = [row[1] for row in samples]
    offsets = [row[2] for row in samples]

    fig, ax = plt.subplots(figsize=(8.4, 2.8), dpi=160)
    fig.patch.set_facecolor("#05070c")
    ax.set_facecolor("#09101a")
    ax.plot(probe_ids, roundtrips, color="#20d3ff", linewidth=2.0, label="Round-trip ms")
    ax.plot(probe_ids, offsets, color="#ff5cb8", linewidth=2.0, label="Quest minus Windows ms")
    ax.set_title("Clock alignment probes", color="white", fontsize=12, loc="left")
    ax.set_xlabel("Probe sequence", color="#9fb2d6")
    ax.set_ylabel("Milliseconds", color="#9fb2d6")
    ax.tick_params(colors="#dce8ff", labelsize=9)
    for spine in ax.spines.values():
        spine.set_color("#1a3c66")
    ax.grid(color="#16304f", alpha=0.55, linewidth=0.8)
    legend = ax.legend(facecolor="#09101a", edgecolor="#1a3c66", framealpha=1.0, fontsize=9)
    for text in legend.get_texts():
        text.set_color("#dce8ff")
    fig.tight_layout()
    fig.savefig(output_path, bbox_inches="tight", facecolor=fig.get_facecolor())
    plt.close(fig)

    return (
        "Clock alignment: "
        f"{len(samples)} probes, round-trip mean {statistics.mean(roundtrips):.1f} ms, "
        f"offset median {statistics.median(offsets):.1f} ms."
    )


def build_pdf(
    session_dir: Path,
    output_pdf: Path,
    settings: dict,
    breathing_summary: str,
    coherence_summary: str,
    clock_summary: str,
    breathing_plot: Path,
    coherence_plot: Path,
    clock_plot: Path,
) -> None:
    output_pdf.parent.mkdir(parents=True, exist_ok=True)
    styles = getSampleStyleSheet()
    title_style = ParagraphStyle(
        "Title",
        parent=styles["Title"],
        textColor=colors.HexColor("#ffffff"),
        fontName="Helvetica-Bold",
        fontSize=20,
        leading=24,
        spaceAfter=8,
    )
    body_style = ParagraphStyle(
        "Body",
        parent=styles["BodyText"],
        textColor=colors.HexColor("#dce8ff"),
        fontName="Helvetica",
        fontSize=9.5,
        leading=13,
        spaceAfter=6,
    )
    micro_style = ParagraphStyle(
        "Micro",
        parent=body_style,
        textColor=colors.HexColor("#9fb2d6"),
        fontSize=8.3,
        leading=11,
    )

    participant_id = settings.get("participantId") or settings.get("participant_id") or "n/a"
    session_id = settings.get("sessionId") or settings.get("session_id") or session_dir.name
    dataset_id = settings.get("datasetId") or settings.get("dataset_id") or "n/a"
    dataset_hash = settings.get("datasetHash") or settings.get("dataset_hash") or "n/a"
    settings_hash = settings.get("settingsHash") or settings.get("settings_hash") or "n/a"

    story = [
        Paragraph("Sussex Validation Capture Preview", title_style),
        Paragraph(
            f"Participant <b>{participant_id}</b> · Session <b>{session_id}</b> · Dataset <b>{dataset_id}</b>",
            body_style,
        ),
        Paragraph(
            f"Windows session folder: <font color='#9fb2d6'>{session_dir}</font><br/>"
            f"Dataset hash: <font color='#9fb2d6'>{dataset_hash}</font><br/>"
            f"Settings hash: <font color='#9fb2d6'>{settings_hash}</font>",
            micro_style,
        ),
        Spacer(1, 4 * mm),
        Paragraph(breathing_summary, body_style),
        Image(str(breathing_plot), width=180 * mm, height=58 * mm),
        Spacer(1, 3 * mm),
        Paragraph(coherence_summary, body_style),
        Image(str(coherence_plot), width=180 * mm, height=58 * mm),
        Spacer(1, 3 * mm),
        Paragraph(clock_summary, body_style),
        Image(str(clock_plot), width=180 * mm, height=58 * mm),
    ]

    document = SimpleDocTemplate(
        str(output_pdf),
        pagesize=A4,
        leftMargin=14 * mm,
        rightMargin=14 * mm,
        topMargin=14 * mm,
        bottomMargin=14 * mm,
        title="Sussex Validation Capture Preview",
        author="Viscereality Companion",
    )
    document.build(story)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--session-dir", required=True)
    parser.add_argument("--output-pdf", required=True)
    args = parser.parse_args()

    session_dir = Path(args.session_dir).resolve()
    output_pdf = Path(args.output_pdf).resolve()

    settings_path = session_dir / "session_settings.json"
    breathing_path = session_dir / "breathing_trace.csv"
    windows_signals_path = session_dir / "signals_long.csv"
    quest_signals_path = session_dir / "device-session-pull" / "signals_long.csv"
    clock_alignment_path = session_dir / "clock_alignment_roundtrip.csv"

    if not session_dir.is_dir():
        raise FileNotFoundError(f"Session folder not found: {session_dir}")
    if not settings_path.is_file():
        raise FileNotFoundError(f"Session settings not found: {settings_path}")

    settings = read_json(settings_path)
    breathing_points = read_breathing_trace(breathing_path) if breathing_path.is_file() else []
    windows_coherence = read_signal_trace(windows_signals_path, "coherence.value01") if windows_signals_path.is_file() else []
    quest_coherence = read_signal_trace(quest_signals_path, "coherence.value01") if quest_signals_path.is_file() else []
    clock_samples = read_clock_alignment(clock_alignment_path) if clock_alignment_path.is_file() else []

    with tempfile.TemporaryDirectory(prefix="sussex_validation_report_") as temp_dir:
        temp_root = Path(temp_dir)
        breathing_plot = temp_root / "breathing.png"
        coherence_plot = temp_root / "coherence.png"
        clock_plot = temp_root / "clock_alignment.png"

        breathing_summary = plot_single_trace(breathing_points, "Breathing volume trace", "#22f0a0", breathing_plot)
        coherence_summary = plot_dual_trace(
            windows_coherence,
            quest_coherence,
            "Coherence alignment (Windows vs Quest)",
            "#20d3ff",
            "#ff5cb8",
            coherence_plot,
        )
        clock_summary = plot_clock_alignment(clock_samples, clock_plot)

        build_pdf(
            session_dir,
            output_pdf,
            settings,
            breathing_summary,
            coherence_summary,
            clock_summary,
            breathing_plot,
            coherence_plot,
            clock_plot,
        )

    print(str(output_pdf))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(str(exc), file=sys.stderr)
        raise
