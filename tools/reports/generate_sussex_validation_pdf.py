from __future__ import annotations

import argparse
import csv
import json
import math
import statistics
import sys
import textwrap
from collections import defaultdict
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.backends.backend_pdf import PdfPages
from matplotlib.patches import FancyBboxPatch


PAGE_SIZE = (11.69, 8.27)
COLORS = {
    "page": "#03060d",
    "panel": "#07101b",
    "panel_alt": "#0a1624",
    "text": "#f5f9ff",
    "muted": "#e7efff",
    "grid": "#17314d",
    "cyan": "#20d3ff",
    "magenta": "#ff4fa8",
    "orange": "#ffb020",
    "green": "#22f0a0",
    "blue": "#3f7cff",
    "danger": "#ff668f",
}


@dataclass(frozen=True)
class PlotSpec:
    title: str
    source_kind: str
    source_name: str
    subtitle: str
    unit_hint: str | None = None


@dataclass(frozen=True)
class ClockAlignmentSample:
    window_kind: str
    probe_sequence: int
    probe_sent_at_utc: float
    echo_received_at_utc: float
    roundtrip_ms: float
    quest_minus_windows_clock_seconds: float
    quest_turnaround_ms: float


def normalize_key(value: str) -> str:
    return "".join(character for character in value.lower() if character.isalnum())


def get_setting(settings: dict, *keys: str, default: str = "n/a") -> str:
    normalized = {normalize_key(str(key)): value for key, value in settings.items()}
    for key in keys:
        normalized_key = normalize_key(key)
        if normalized_key in normalized:
            value = normalized[normalized_key]
            if value is None or value == "":
                continue
            return str(value)
    return default


def parse_iso_timestamp(value: str) -> float:
    normalized = value.strip()
    if normalized.endswith("Z"):
        normalized = normalized[:-1] + "+00:00"
    return datetime.fromisoformat(normalized).timestamp()


def format_utc(value: float | None) -> str:
    if value is None:
        return "n/a"
    return datetime.fromtimestamp(value, UTC).strftime("%Y-%m-%d %H:%M:%S UTC")


def parse_numeric_like(raw: str) -> float:
    text = raw.strip()
    lowered = text.lower()
    if lowered == "true":
        return 1.0
    if lowered == "false":
        return 0.0
    return float(text)


def read_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def read_signal_traces(path: Path) -> tuple[dict[str, list[tuple[float, float]]], dict[str, str], dict[str, list[str]]]:
    traces: dict[str, list[tuple[float, float]]] = defaultdict(list)
    units: dict[str, str] = {}
    texts: dict[str, list[str]] = defaultdict(list)

    if not path.is_file():
        return traces, units, texts

    with path.open("r", encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            signal_name = row.get("signal_name", "").strip()
            if not signal_name:
                continue

            timestamp = (row.get("source_timestamp_utc") or row.get("recorded_at_utc") or "").strip()
            parsed_timestamp = None
            if timestamp:
                try:
                    parsed_timestamp = parse_iso_timestamp(timestamp)
                except ValueError:
                    parsed_timestamp = None

            raw_numeric = row.get("value_numeric", "").strip()
            raw_text = row.get("value_text", "").strip()
            unit = row.get("unit", "").strip()
            if unit:
                units[signal_name] = unit
            if raw_text:
                texts[signal_name].append(raw_text)

            if parsed_timestamp is None or not raw_numeric:
                continue

            try:
                numeric_value = parse_numeric_like(raw_numeric)
            except ValueError:
                continue

            traces[signal_name].append((parsed_timestamp, numeric_value))

    for series in traces.values():
        series.sort(key=lambda item: item[0])
    return traces, units, texts


def read_breathing_traces(path: Path) -> tuple[dict[str, list[tuple[float, float]]], dict[str, str]]:
    traces: dict[str, list[tuple[float, float]]] = defaultdict(list)
    units: dict[str, str] = {}

    if not path.is_file():
        return traces, units

    with path.open("r", encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle)
        field_names = reader.fieldnames or []
        metric_fields = [
            name
            for name in field_names
            if name not in {"participant_id", "session_id", "dataset_id", "recorded_at_utc", "source_timestamp_utc"}
        ]

        for row in reader:
            timestamp_text = (row.get("source_timestamp_utc") or row.get("recorded_at_utc") or "").strip()
            if not timestamp_text:
                continue
            try:
                parsed_timestamp = parse_iso_timestamp(timestamp_text)
            except ValueError:
                continue

            for field_name in metric_fields:
                raw_value = row.get(field_name, "").strip()
                if not raw_value:
                    continue
                try:
                    numeric_value = parse_numeric_like(raw_value)
                except ValueError:
                    continue

                traces[field_name].append((parsed_timestamp, numeric_value))
                if field_name.endswith("01") or field_name.endswith("_progress01"):
                    units.setdefault(field_name, "unit01")
                elif field_name.endswith("_raw"):
                    units.setdefault(field_name, "units")
                elif field_name.endswith("_active") or field_name.endswith("_calibrated"):
                    units.setdefault(field_name, "bool")
                else:
                    units.setdefault(field_name, "value")

    for series in traces.values():
        series.sort(key=lambda item: item[0])
    return traces, units


def read_session_events(path: Path) -> list[tuple[float, str, str]]:
    events: list[tuple[float, str, str]] = []
    if not path.is_file():
        return events

    with path.open("r", encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            timestamp_text = row.get("recorded_at_utc", "").strip()
            event_name = row.get("event_name", "").strip()
            detail = row.get("event_detail", "").strip()
            if not timestamp_text or not event_name:
                continue
            try:
                parsed_timestamp = parse_iso_timestamp(timestamp_text)
            except ValueError:
                continue
            events.append((parsed_timestamp, event_name, detail))

    events.sort(key=lambda item: item[0])
    return events


def read_clock_alignment(path: Path) -> list[ClockAlignmentSample]:
    samples: list[ClockAlignmentSample] = []
    if not path.is_file():
        return samples

    with path.open("r", encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            try:
                probe_sequence = int(float(row.get("probe_sequence", "").strip()))
                probe_sent_at_utc = parse_iso_timestamp(row.get("probe_sent_at_utc", "").strip())
                echo_received_at_utc = parse_iso_timestamp(row.get("echo_received_at_utc", "").strip())
                roundtrip_seconds = float(row.get("roundtrip_seconds", "").strip())
                quest_minus_windows_clock_seconds = float(row.get("quest_minus_windows_clock_seconds", "").strip())
                quest_received_lsl_seconds = float(row.get("quest_received_lsl_seconds", "").strip())
                quest_echo_lsl_seconds = float(row.get("quest_echo_lsl_seconds", "").strip())
            except ValueError:
                continue

            samples.append(
                ClockAlignmentSample(
                    window_kind=row.get("window_kind", "").strip(),
                    probe_sequence=probe_sequence,
                    probe_sent_at_utc=probe_sent_at_utc,
                    echo_received_at_utc=echo_received_at_utc,
                    roundtrip_ms=roundtrip_seconds * 1000.0,
                    quest_minus_windows_clock_seconds=quest_minus_windows_clock_seconds,
                    quest_turnaround_ms=max(0.0, (quest_echo_lsl_seconds - quest_received_lsl_seconds) * 1000.0),
                )
            )

    samples.sort(key=lambda item: item.probe_sequence)
    return samples


def seconds_since(points: list[tuple[float, float]], session_start_utc: float) -> list[tuple[float, float]]:
    return [(timestamp - session_start_utc, value) for timestamp, value in points]


def collect_all_timestamps(
    signal_traces: list[dict[str, list[tuple[float, float]]]],
    breathing_traces: list[dict[str, list[tuple[float, float]]]],
    event_traces: list[list[tuple[float, str, str]]],
) -> list[float]:
    timestamps: list[float] = []
    for group in signal_traces + breathing_traces:
        for series in group.values():
            timestamps.extend(point[0] for point in series)
    for events in event_traces:
        timestamps.extend(event[0] for event in events)
    return timestamps


def format_duration(seconds_value: float | None) -> str:
    if seconds_value is None:
        return "n/a"
    if seconds_value < 60.0:
        return f"{seconds_value:.1f}s"
    minutes = int(seconds_value // 60)
    seconds = seconds_value - minutes * 60
    return f"{minutes}m {seconds:.1f}s"


def duration_from_points(points: list[tuple[float, float]]) -> float | None:
    if len(points) < 2:
        return None
    return points[-1][0] - points[0][0]


def short_hash(value: str, length: int = 12) -> str:
    if not value or value == "n/a":
        return "n/a"
    return value[:length]


def shorten_middle(value: str, max_length: int = 88) -> str:
    if len(value) <= max_length:
        return value
    keep = max_length - 3
    front = keep // 2
    back = keep - front
    return value[:front] + "..." + value[-back:]


def add_card(ax, x: float, y: float, w: float, h: float, title: str, lines: list[str], edge_color: str) -> None:
    card = FancyBboxPatch(
        (x, y),
        w,
        h,
        boxstyle="round,pad=0.008,rounding_size=0.02",
        linewidth=1.4,
        edgecolor=edge_color,
        facecolor=COLORS["panel"],
        transform=ax.transAxes,
        zorder=2,
    )
    ax.add_patch(card)
    ax.text(
        x + 0.015,
        y + h - 0.03,
        title,
        transform=ax.transAxes,
        va="top",
        ha="left",
        color=COLORS["text"],
        fontsize=11.5,
        fontweight="bold",
        zorder=3,
    )

    line_y = y + h - 0.06
    max_chars = max(24, int(w * 118))
    rendered_lines: list[str] = []
    for line in lines:
        wrapped = textwrap.wrap(line, width=max_chars, break_long_words=False, break_on_hyphens=False)
        rendered_lines.extend(wrapped if wrapped else [""])

    for line in rendered_lines:
        ax.text(
            x + 0.015,
            line_y,
            line,
            transform=ax.transAxes,
            va="top",
            ha="left",
            color=COLORS["muted"] if not line.startswith("! ") else COLORS["danger"],
            fontsize=8.8,
            zorder=3,
        )
        line_y -= 0.03


def apply_axis_style(ax, title: str, unit_label: str, subtitle: str = "") -> None:
    ax.set_facecolor(COLORS["panel_alt"])
    ax.set_title(title, color=COLORS["text"], fontsize=11.3, loc="left", pad=4, fontweight="bold")
    ax.tick_params(colors=COLORS["muted"], labelsize=8.5)
    for spine in ax.spines.values():
        spine.set_color(COLORS["grid"])
        spine.set_linewidth(1.0)
    ax.grid(color=COLORS["grid"], alpha=0.45, linewidth=0.8)
    ax.set_xlabel("s since session start", color=COLORS["muted"], fontsize=8.8)
    ax.set_ylabel(unit_label or "value", color=COLORS["muted"], fontsize=8.8)


def compute_y_limits(
    signal_name: str,
    unit: str,
    windows_points: list[tuple[float, float]],
    quest_points: list[tuple[float, float]],
) -> tuple[float, float]:
    values = [point[1] for point in windows_points] + [point[1] for point in quest_points]
    lower_name = signal_name.lower()
    normalized_markers = ("value01", "progress01", "tracking01", "confidence01", "acceptance01")

    if not values:
        return 0.0, 1.0
    if unit == "bool" or lower_name.endswith("_active") or lower_name.endswith("_calibrated"):
        return -0.05, 1.05
    if unit == "quaternion":
        return -1.05, 1.05
    if any(marker in lower_name for marker in normalized_markers):
        return 0.0, 1.0

    minimum = min(values)
    maximum = max(values)
    if math.isclose(minimum, maximum):
        padding = 0.1 if abs(minimum) <= 1.0 else max(abs(minimum) * 0.1, 1.0)
        return minimum - padding, maximum + padding

    spread = maximum - minimum
    padding = spread * 0.12
    return minimum - padding, maximum + padding


def plot_overlay_panel(
    ax,
    title: str,
    subtitle: str,
    unit: str,
    windows_points: list[tuple[float, float]],
    quest_points: list[tuple[float, float]],
) -> None:
    apply_axis_style(ax, title, unit, subtitle)

    if not windows_points and not quest_points:
        ax.text(
            0.5,
            0.5,
            "No samples",
            transform=ax.transAxes,
            ha="center",
            va="center",
            color=COLORS["muted"],
            fontsize=11,
        )
        ax.set_xticks([])
        ax.set_yticks([])
        return

    if windows_points:
        ax.plot(
            [point[0] for point in windows_points],
            [point[1] for point in windows_points],
            color=COLORS["blue"],
            linewidth=1.9,
            label=f"Windows ({len(windows_points)})",
        )
        if len(windows_points) == 1:
            ax.scatter(
                [windows_points[0][0]],
                [windows_points[0][1]],
                color=COLORS["blue"],
                s=28,
                zorder=4,
            )
    if quest_points:
        ax.plot(
            [point[0] for point in quest_points],
            [point[1] for point in quest_points],
            color=COLORS["orange"],
            linewidth=1.9,
            label=f"Quest ({len(quest_points)})",
        )
        if len(quest_points) == 1:
            ax.scatter(
                [quest_points[0][0]],
                [quest_points[0][1]],
                color=COLORS["orange"],
                s=28,
                zorder=4,
            )

    y_min, y_max = compute_y_limits(title, unit, windows_points, quest_points)
    ax.set_ylim(y_min, y_max)
    x_max = max([point[0] for point in windows_points] + [point[0] for point in quest_points] + [1.0])
    ax.set_xlim(left=0.0, right=max(1.0, x_max))
    legend = ax.legend(facecolor=COLORS["panel_alt"], edgecolor=COLORS["grid"], fontsize=8.2, loc="upper right")
    for text in legend.get_texts():
        text.set_color(COLORS["text"])


def build_cover_page(
    pdf: PdfPages,
    session_dir: Path,
    settings: dict,
    windows_signals: dict[str, list[tuple[float, float]]],
    quest_signals: dict[str, list[tuple[float, float]]],
    windows_breathing: dict[str, list[tuple[float, float]]],
    quest_breathing: dict[str, list[tuple[float, float]]],
    windows_events: list[tuple[float, str, str]],
    quest_events: list[tuple[float, str, str]],
    clock_samples: list[ClockAlignmentSample],
    session_start_utc: float,
) -> None:
    figure = plt.figure(figsize=PAGE_SIZE, facecolor=COLORS["page"])
    axis = figure.add_axes([0.0, 0.0, 1.0, 1.0])
    axis.axis("off")
    axis.set_facecolor(COLORS["page"])

    participant_id = get_setting(settings, "participantId", "ParticipantId")
    session_id = get_setting(settings, "sessionId", "SessionId", default=session_dir.name)
    dataset_id = get_setting(settings, "datasetId", "DatasetId")
    dataset_hash = get_setting(settings, "datasetHash", "DatasetHash")
    settings_hash = get_setting(settings, "settingsHash", "SettingsHash")
    package_id = get_setting(settings, "packageId", "PackageId")
    package_version = get_setting(settings, "appVersionName", "AppVersionName")
    selector = get_setting(settings, "questSelector", "QuestSelector")
    profile_label = get_setting(settings, "deviceProfileLabel", "DeviceProfileLabel")
    headset_build = get_setting(settings, "headsetSoftwareVersion", "HeadsetSoftwareVersion")
    headset_display = get_setting(settings, "headsetDisplayId", "HeadsetDisplayId")
    device_session_path = session_dir / "device-session-pull"
    start_text = get_setting(settings, "sessionStartedAtUtc", "SessionStartedAtUtc")
    end_text = get_setting(settings, "sessionEndedAtUtc", "SessionEndedAtUtc")

    session_end_utc = None
    if start_text != "n/a" and end_text != "n/a":
        try:
            session_end_utc = parse_iso_timestamp(end_text)
        except ValueError:
            session_end_utc = None

    local_signal_count = sum(len(series) for series in windows_signals.values())
    quest_signal_count = sum(len(series) for series in quest_signals.values())
    local_breathing_count = max((len(series) for series in windows_breathing.values()), default=0)
    quest_breathing_count = max((len(series) for series in quest_breathing.values()), default=0)
    local_duration = max((duration_from_points(series) or 0.0) for series in windows_signals.values()) if windows_signals else 0.0
    quest_duration = max((duration_from_points(series) or 0.0) for series in quest_signals.values()) if quest_signals else 0.0

    expected_probes = 0
    try:
        expected_probes = int(
            round(
                float(get_setting(settings, "clockAlignmentDurationSeconds", "ClockAlignmentDurationSeconds", default="0"))
                * 1000.0
                / float(
                    get_setting(
                        settings,
                        "clockAlignmentProbeIntervalMilliseconds",
                        "ClockAlignmentProbeIntervalMilliseconds",
                        default="250",
                    )
                )
            )
        )
    except ValueError:
        expected_probes = 0

    start_burst_samples = [sample for sample in clock_samples if sample.window_kind == "StartBurst"]
    burst_samples = start_burst_samples or clock_samples

    if burst_samples:
        roundtrips = [sample.roundtrip_ms for sample in burst_samples]
        offsets = [sample.quest_minus_windows_clock_seconds for sample in burst_samples]
        coverage = f"{len(burst_samples)} echoed"
        if expected_probes > 0:
            coverage = f"{len(burst_samples)} echoed / {expected_probes} sent"
        alignment_lines = [
            f"Round-trip mean {statistics.mean(roundtrips):.1f} ms, median {statistics.median(roundtrips):.1f} ms",
            f"Clock-origin offset {statistics.median(offsets):.3f} s",
            coverage,
        ]
        if start_burst_samples and len(start_burst_samples) != len(clock_samples):
            background_count = sum(1 for sample in clock_samples if sample.window_kind == "BackgroundSparse")
            end_count = sum(1 for sample in clock_samples if sample.window_kind == "EndBurst")
            alignment_lines.append(f"Background echoes {background_count}, end burst echoes {end_count}")
    else:
        alignment_lines = ["Clock alignment file missing or empty.", "No RTT/offset estimates available."]

    axis.text(0.045, 0.952, "Sussex Session Review", color=COLORS["text"], fontsize=24, fontweight="bold", va="top")
    axis.text(
        0.045,
        0.905,
        "Dark validation report for the operator-side session capture and Quest pullback.",
        color=COLORS["muted"],
        fontsize=10.5,
        va="top",
    )
    axis.text(0.955, 0.952, "Viscereality Companion", color=COLORS["cyan"], fontsize=12, ha="right", va="top")

    add_card(
        axis,
        0.04,
        0.62,
        0.28,
        0.22,
        "Session identity",
        [
            f"Participant {participant_id}",
            f"Session {session_id}",
            f"Started {format_utc(session_start_utc)}",
            f"Ended {format_utc(session_end_utc)}" if session_end_utc else "Ended n/a",
            f"Duration {format_duration((session_end_utc - session_start_utc) if session_end_utc else None)}",
        ],
        COLORS["cyan"],
    )
    add_card(
        axis,
        0.36,
        0.62,
        0.28,
        0.22,
        "Runtime baseline",
        [
            package_id,
            f"Version {package_version} | APK {short_hash(get_setting(settings, 'apkSha256', 'ApkSha256'), 16)}",
            f"Build {headset_build}",
            f"Display {headset_display}",
            f"Quest {selector}",
        ],
        COLORS["cyan"],
    )
    add_card(
        axis,
        0.68,
        0.62,
        0.28,
        0.22,
        "Clock alignment",
        alignment_lines,
        COLORS["orange"],
    )

    add_card(
        axis,
        0.04,
        0.36,
        0.45,
        0.17,
        "Recorder coverage",
        [
            f"Windows signals {local_signal_count} rows across {format_duration(local_duration)}",
            f"Quest signals {quest_signal_count} rows across {format_duration(quest_duration)}",
            f"Windows breathing {local_breathing_count} rows, Quest breathing {quest_breathing_count} rows",
            f"Windows events {len(windows_events)}, Quest events {len(quest_events)}",
        ],
        COLORS["green"],
    )

    key_windows_events = {name: timestamp for timestamp, name, _ in windows_events}
    milestone_lines = []
    for event_name, label in [
        ("experiment.start_command", "Start command"),
        ("recording.device_confirmation", "Quest recorder confirmed"),
        ("clock_alignment.result", "Clock alignment finished"),
        ("experiment.end_command", "End command"),
        ("recording.device_stop_confirmation", "Quest recorder stopped"),
    ]:
        if event_name in key_windows_events:
            milestone_lines.append(f"{label} +{key_windows_events[event_name] - session_start_utc:.1f}s")
    if not milestone_lines:
        milestone_lines.append("Run milestones unavailable in session_events.csv")

    add_card(
        axis,
        0.51,
        0.36,
        0.45,
        0.17,
        "Run milestones",
        milestone_lines[:4],
        COLORS["cyan"],
    )

    path_lines = [
        f"Windows session folder: {shorten_middle(str(session_dir))}",
        f"Pulled Quest folder: {shorten_middle(str(device_session_path))}",
        f"Dataset hash {short_hash(dataset_hash, 32)} | Settings hash {short_hash(settings_hash, 32)}",
    ]
    add_card(axis, 0.04, 0.08, 0.92, 0.21, "Artifact paths", path_lines, COLORS["cyan"])

    pdf.savefig(figure, facecolor=figure.get_facecolor())
    plt.close(figure)


def build_signal_page(
    pdf: PdfPages,
    title: str,
    plot_specs: list[PlotSpec],
    session_start_utc: float,
    windows_signal_traces: dict[str, list[tuple[float, float]]],
    quest_signal_traces: dict[str, list[tuple[float, float]]],
    windows_signal_units: dict[str, str],
    quest_signal_units: dict[str, str],
    windows_breathing_traces: dict[str, list[tuple[float, float]]],
    quest_breathing_traces: dict[str, list[tuple[float, float]]],
    windows_breathing_units: dict[str, str],
    quest_breathing_units: dict[str, str],
) -> None:
    figure, axes = plt.subplots(2, 2, figsize=PAGE_SIZE, facecolor=COLORS["page"])
    axes = axes.flatten()
    figure.subplots_adjust(left=0.07, right=0.965, top=0.79, bottom=0.08, hspace=0.34, wspace=0.18)
    figure.suptitle(title, color=COLORS["text"], fontsize=20, fontweight="bold", y=0.955)
    figure.text(
        0.07,
        0.865,
        "Windows and Quest traces share the same session-start time base.",
        color=COLORS["muted"],
        fontsize=10.2,
    )

    for axis, spec in zip(axes, plot_specs):
        if spec.source_kind == "signals":
            windows_source = windows_signal_traces
            quest_source = quest_signal_traces
            windows_units = windows_signal_units
            quest_units = quest_signal_units
        else:
            windows_source = windows_breathing_traces
            quest_source = quest_breathing_traces
            windows_units = windows_breathing_units
            quest_units = quest_breathing_units

        windows_points = seconds_since(windows_source.get(spec.source_name, []), session_start_utc)
        quest_points = seconds_since(quest_source.get(spec.source_name, []), session_start_utc)
        unit = spec.unit_hint or windows_units.get(spec.source_name) or quest_units.get(spec.source_name) or "value"
        plot_overlay_panel(axis, spec.title, spec.subtitle, unit, windows_points, quest_points)

    for axis in axes[len(plot_specs) :]:
        axis.axis("off")
        axis.set_facecolor(COLORS["page"])

    figure.text(0.07, 0.028, "Blue = Windows, Orange = Quest", color=COLORS["muted"], fontsize=9.3)
    pdf.savefig(figure, facecolor=figure.get_facecolor())
    plt.close(figure)


def build_clock_alignment_page(pdf: PdfPages, clock_samples: list[ClockAlignmentSample], settings: dict) -> None:
    figure, axes = plt.subplots(2, 2, figsize=PAGE_SIZE, facecolor=COLORS["page"])
    axes = axes.flatten()
    figure.subplots_adjust(left=0.07, right=0.965, top=0.79, bottom=0.09, hspace=0.34, wspace=0.20)
    figure.suptitle("Clock alignment review", color=COLORS["text"], fontsize=20, fontweight="bold", y=0.955)
    figure.text(
        0.07,
        0.875,
        "This page uses the dedicated 10-second Sussex alignment probe recorded at experiment start.",
        color=COLORS["muted"],
        fontsize=10.2,
    )
    figure.text(
        0.07,
        0.848,
        "Early high-RTT probes usually reflect fresh probe-stream startup; the offset estimate uses the lowest-latency quartile.",
        color=COLORS["muted"],
        fontsize=9.4,
    )

    page_samples = [sample for sample in clock_samples if sample.window_kind == "StartBurst"] or clock_samples

    if not page_samples:
        for axis in axes:
            axis.set_facecolor(COLORS["panel_alt"])
            axis.axis("off")
            axis.text(0.5, 0.5, "No clock-alignment samples", ha="center", va="center", color=COLORS["muted"])
        pdf.savefig(figure, facecolor=figure.get_facecolor())
        plt.close(figure)
        return

    probe_ids = [sample.probe_sequence for sample in page_samples]
    roundtrips = [sample.roundtrip_ms for sample in page_samples]
    offsets_seconds = [sample.quest_minus_windows_clock_seconds for sample in page_samples]
    median_offset = statistics.median(offsets_seconds)
    offset_residual_ms = [(value - median_offset) * 1000.0 for value in offsets_seconds]
    turnarounds = [sample.quest_turnaround_ms for sample in page_samples]
    histogram_bins = min(12, max(4, math.ceil(math.sqrt(len(roundtrips)))))

    apply_axis_style(axes[0], "Round-trip latency", "ms", "Probe echo return time")
    axes[0].plot(probe_ids, roundtrips, color=COLORS["cyan"], linewidth=2.0)
    axes[0].scatter(probe_ids, roundtrips, color=COLORS["cyan"], s=16)
    axes[0].set_xlim(left=min(probe_ids), right=max(probe_ids))
    axes[0].set_xlabel("probe sequence", color=COLORS["muted"], fontsize=8.8)

    apply_axis_style(axes[1], "Offset residual", "ms", "Quest-minus-Windows offset after median subtraction")
    axes[1].plot(probe_ids, offset_residual_ms, color=COLORS["magenta"], linewidth=2.0)
    axes[1].axhline(0.0, color=COLORS["muted"], linewidth=0.8, linestyle="--", alpha=0.7)
    axes[1].set_xlim(left=min(probe_ids), right=max(probe_ids))
    axes[1].set_xlabel("probe sequence", color=COLORS["muted"], fontsize=8.8)

    apply_axis_style(axes[2], "Quest echo turnaround", "ms", "Quest receive-to-echo pipeline")
    axes[2].plot(probe_ids, turnarounds, color=COLORS["green"], linewidth=2.0)
    axes[2].scatter(probe_ids, turnarounds, color=COLORS["green"], s=16)
    axes[2].set_xlim(left=min(probe_ids), right=max(probe_ids))
    axes[2].set_xlabel("probe sequence", color=COLORS["muted"], fontsize=8.8)

    apply_axis_style(axes[3], "Round-trip distribution", "count", "Probe histogram")
    axes[3].hist(roundtrips, bins=histogram_bins, color=COLORS["orange"], edgecolor=COLORS["panel"], alpha=0.95)
    axes[3].set_xlabel("round-trip ms", color=COLORS["muted"], fontsize=8.8)

    expected_probes = 0
    try:
        expected_probes = int(
            round(
                float(get_setting(settings, "clockAlignmentDurationSeconds", "ClockAlignmentDurationSeconds", default="0"))
                * 1000.0
                / float(
                    get_setting(
                        settings,
                        "clockAlignmentProbeIntervalMilliseconds",
                        "ClockAlignmentProbeIntervalMilliseconds",
                        default="250",
                    )
                )
            )
        )
    except ValueError:
        expected_probes = 0

    summary = (
        f"Echo coverage {len(page_samples)}"
        + (f" / {expected_probes}" if expected_probes > 0 else "")
        + f" | RTT mean {statistics.mean(roundtrips):.1f} ms"
        + f" | RTT span {min(roundtrips):.1f} to {max(roundtrips):.1f} ms"
        + f" | Clock-origin offset {median_offset:.3f} s"
    )
    figure.text(0.07, 0.035, summary, color=COLORS["muted"], fontsize=9.4)
    pdf.savefig(figure, facecolor=figure.get_facecolor())
    plt.close(figure)


def build_report(session_dir: Path, output_pdf: Path) -> None:
    settings_path = session_dir / "session_settings.json"
    windows_signals_path = session_dir / "signals_long.csv"
    quest_signals_path = session_dir / "device-session-pull" / "signals_long.csv"
    windows_breathing_path = session_dir / "breathing_trace.csv"
    quest_breathing_path = session_dir / "device-session-pull" / "breathing_trace.csv"
    windows_events_path = session_dir / "session_events.csv"
    quest_events_path = session_dir / "device-session-pull" / "session_events.csv"
    clock_alignment_path = session_dir / "clock_alignment_roundtrip.csv"

    if not session_dir.is_dir():
        raise FileNotFoundError(f"Session folder not found: {session_dir}")
    if not settings_path.is_file():
        raise FileNotFoundError(f"Session settings not found: {settings_path}")

    settings = read_json(settings_path)
    windows_signals, windows_signal_units, _ = read_signal_traces(windows_signals_path)
    quest_signals, quest_signal_units, _ = read_signal_traces(quest_signals_path)
    windows_breathing, windows_breathing_units = read_breathing_traces(windows_breathing_path)
    quest_breathing, quest_breathing_units = read_breathing_traces(quest_breathing_path)
    windows_events = read_session_events(windows_events_path)
    quest_events = read_session_events(quest_events_path)
    clock_samples = read_clock_alignment(clock_alignment_path)

    session_start_text = get_setting(settings, "sessionStartedAtUtc", "SessionStartedAtUtc", default="")
    session_start_utc = None
    if session_start_text:
        try:
            session_start_utc = parse_iso_timestamp(session_start_text)
        except ValueError:
            session_start_utc = None

    if session_start_utc is None:
        timestamps = collect_all_timestamps(
            [windows_signals, quest_signals],
            [windows_breathing, quest_breathing],
            [windows_events, quest_events],
        )
        if not timestamps:
            raise ValueError("No timestamped session data found.")
        session_start_utc = min(timestamps)

    plot_pages = [
        (
            "Biofeedback alignment",
            [
                PlotSpec("coherence.value01", "signals", "coherence.value01", "Shared twin-state coherence"),
                PlotSpec("heartbeat.value01", "signals", "heartbeat.value01", "Normalized heartbeat envelope"),
                PlotSpec("heartbeat.packet_value01", "signals", "heartbeat.packet_value01", "LSL packet payload"),
                PlotSpec("heartbeat.real_beat_value01", "signals", "heartbeat.real_beat_value01", "Beat trigger ramp"),
            ],
        ),
        (
            "Breathing calibration traces",
            [
                PlotSpec("breath_volume01", "breathing", "breath_volume01", "Recorder-specific breathing trace"),
                PlotSpec("breathing.value01", "signals", "breathing.value01", "Runtime breathing mirror"),
                PlotSpec(
                    "sphere_radius_progress01",
                    "breathing",
                    "sphere_radius_progress01",
                    "Breathing recorder sphere progress",
                ),
                PlotSpec(
                    "sphere_radius.progress01",
                    "signals",
                    "sphere_radius.progress01",
                    "Runtime sphere progress mirror",
                ),
            ],
        ),
        (
            "Breathing geometry",
            [
                PlotSpec("sphere_radius_raw", "breathing", "sphere_radius_raw", "Breathing recorder raw sphere radius"),
                PlotSpec("sphere_radius.raw", "signals", "sphere_radius.raw", "Runtime raw sphere radius mirror"),
                PlotSpec(
                    "controller_calibrated",
                    "breathing",
                    "controller_calibrated",
                    "Breathing recorder controller calibrated",
                ),
            ],
        ),
        (
            "LSL counters",
            [
                PlotSpec("lsl.sample_count", "signals", "lsl.sample_count", "LSL sample counter"),
                PlotSpec("lsl.latest_timestamp_seconds", "signals", "lsl.latest_timestamp_seconds", "Latest LSL timestamp"),
            ],
        ),
        (
            "Headset and controller position",
            [
                PlotSpec("headset.position.x", "signals", "headset.position.x", "Headset X"),
                PlotSpec("headset.position.y", "signals", "headset.position.y", "Headset Y"),
                PlotSpec("headset.position.z", "signals", "headset.position.z", "Headset Z"),
                PlotSpec("controller.position.x", "signals", "controller.position.x", "Controller X"),
            ],
        ),
        (
            "Controller position",
            [
                PlotSpec("controller.position.y", "signals", "controller.position.y", "Controller Y"),
                PlotSpec("controller.position.z", "signals", "controller.position.z", "Controller Z"),
            ],
        ),
        (
            "Headset rotation",
            [
                PlotSpec("headset.rotation.qw", "signals", "headset.rotation.qw", "Headset rotation qw"),
                PlotSpec("headset.rotation.qx", "signals", "headset.rotation.qx", "Headset rotation qx"),
                PlotSpec("headset.rotation.qy", "signals", "headset.rotation.qy", "Headset rotation qy"),
                PlotSpec("headset.rotation.qz", "signals", "headset.rotation.qz", "Headset rotation qz"),
            ],
        ),
        (
            "Controller rotation",
            [
                PlotSpec("controller.rotation.qw", "signals", "controller.rotation.qw", "Controller rotation qw"),
                PlotSpec("controller.rotation.qx", "signals", "controller.rotation.qx", "Controller rotation qx"),
                PlotSpec("controller.rotation.qy", "signals", "controller.rotation.qy", "Controller rotation qy"),
                PlotSpec("controller.rotation.qz", "signals", "controller.rotation.qz", "Controller rotation qz"),
            ],
        ),
    ]

    output_pdf.parent.mkdir(parents=True, exist_ok=True)
    with PdfPages(str(output_pdf)) as pdf:
        metadata = pdf.infodict()
        metadata["Title"] = "Sussex Session Review"
        metadata["Author"] = "Viscereality Companion"
        metadata["Subject"] = "Sussex validation capture report"
        metadata["Keywords"] = "sussex, quest, validation, lsl, pdf"

        build_cover_page(
            pdf,
            session_dir,
            settings,
            windows_signals,
            quest_signals,
            windows_breathing,
            quest_breathing,
            windows_events,
            quest_events,
            clock_samples,
            session_start_utc,
        )
        for page_title, specs in plot_pages:
            build_signal_page(
                pdf,
                page_title,
                specs,
                session_start_utc,
                windows_signals,
                quest_signals,
                windows_signal_units,
                quest_signal_units,
                windows_breathing,
                quest_breathing,
                windows_breathing_units,
                quest_breathing_units,
            )
        build_clock_alignment_page(pdf, clock_samples, settings)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--session-dir", required=True)
    parser.add_argument("--output-pdf", required=True)
    args = parser.parse_args()

    session_dir = Path(args.session_dir).resolve()
    output_pdf = Path(args.output_pdf).resolve()

    build_report(session_dir, output_pdf)
    print(str(output_pdf))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(str(exc), file=sys.stderr)
        raise
