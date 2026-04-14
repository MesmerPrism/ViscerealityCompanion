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
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

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


@dataclass(frozen=True)
class UpstreamLslSample:
    recorded_at_utc: float
    observed_local_clock_seconds: float
    stream_sample_timestamp_seconds: float
    value_numeric: float
    sequence: int


@dataclass(frozen=True)
class TimingMarker:
    recorded_at_utc: float
    marker_name: str
    sample_sequence: int | None
    source_lsl_timestamp_seconds: float
    quest_local_clock_seconds: float
    value01: float | None
    aux_value: float | None


@dataclass(frozen=True)
class QuestPacketTiming:
    source_lsl_timestamp_seconds: float
    sample_sequence: int | None
    packet_value01: float | None
    heartbeat_packet_receive_at_quest_local: float | None
    coherence_packet_receive_at_quest_local: float | None
    heartbeat_real_beat_publish_at_quest_local: float | None
    coherence_value_publish_at_quest_local: float | None
    orbit_radius_peak_at_quest_local: float | None
    orbit_radius_peak_value01: float | None
    orbit_radius_peak_aux_value: float | None


@dataclass(frozen=True)
class PacketTimingMatch:
    source_lsl_timestamp_seconds: float
    relative_seconds: float
    windows_sequence: int | None
    quest_sample_sequence: int | None
    windows_packet_value01: float | None
    quest_packet_value01: float | None
    windows_observed_local_clock_seconds: float | None
    quest_heartbeat_packet_receive_at_quest_local: float | None
    quest_coherence_value_publish_at_quest_local: float | None
    quest_orbit_radius_peak_at_quest_local: float | None
    orbit_radius_peak_value01: float | None
    orbit_radius_peak_aux_value: float | None
    windows_receive_latency_ms: float | None
    quest_receive_latency_ms: float | None
    coherence_publish_latency_ms: float | None
    orbit_peak_latency_ms: float | None
    quest_receive_to_orbit_peak_ms: float | None


@dataclass(frozen=True)
class PacketTimingAnalysis:
    matches: list[PacketTimingMatch]
    windows_overlap: list[UpstreamLslSample]
    quest_overlap: list[QuestPacketTiming]
    windows_only: list[UpstreamLslSample]
    quest_only: list[QuestPacketTiming]
    overlap_start: float | None
    overlap_end: float | None
    quest_minus_windows_clock_seconds: float | None


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
    return datetime.fromtimestamp(value, timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")


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


def read_upstream_lsl_samples(path: Path) -> list[UpstreamLslSample]:
    samples: list[UpstreamLslSample] = []
    if not path.is_file():
        return samples

    with path.open("r", encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            try:
                recorded_at_utc = parse_iso_timestamp(row.get("recorded_at_utc", "").strip())
                observed_local_clock_seconds = float(row.get("observed_local_clock_seconds", "").strip())
                stream_sample_timestamp_seconds = float(row.get("stream_sample_timestamp_seconds", "").strip())
                value_numeric = float(row.get("value_numeric", "").strip())
                sequence = int(float(row.get("sequence", "").strip()))
            except ValueError:
                continue

            samples.append(
                UpstreamLslSample(
                    recorded_at_utc=recorded_at_utc,
                    observed_local_clock_seconds=observed_local_clock_seconds,
                    stream_sample_timestamp_seconds=stream_sample_timestamp_seconds,
                    value_numeric=value_numeric,
                    sequence=sequence,
                )
            )

    samples.sort(key=lambda item: item.stream_sample_timestamp_seconds)
    return samples


def parse_optional_float(value: str) -> float | None:
    text = value.strip()
    if not text:
        return None
    try:
        return float(text)
    except ValueError:
        return None


def parse_optional_int(value: str) -> int | None:
    text = value.strip()
    if not text:
        return None
    try:
        return int(float(text))
    except ValueError:
        return None


def read_timing_markers(path: Path) -> list[TimingMarker]:
    markers: list[TimingMarker] = []
    if not path.is_file():
        return markers

    with path.open("r", encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            source_timestamp = parse_optional_float(row.get("source_lsl_timestamp_seconds", ""))
            quest_clock = parse_optional_float(row.get("quest_local_clock_seconds", ""))
            if source_timestamp is None or quest_clock is None:
                continue

            try:
                recorded_at_utc = parse_iso_timestamp(row.get("recorded_at_utc", "").strip())
            except ValueError:
                continue

            markers.append(
                TimingMarker(
                    recorded_at_utc=recorded_at_utc,
                    marker_name=row.get("marker_name", "").strip(),
                    sample_sequence=parse_optional_int(row.get("sample_sequence", "")),
                    source_lsl_timestamp_seconds=source_timestamp,
                    quest_local_clock_seconds=quest_clock,
                    value01=parse_optional_float(row.get("value01", "")),
                    aux_value=parse_optional_float(row.get("aux_value", "")),
                )
            )

    markers.sort(key=lambda item: (item.source_lsl_timestamp_seconds, item.quest_local_clock_seconds, item.marker_name))
    return markers


def group_quest_packet_timings(markers: Iterable[TimingMarker]) -> list[QuestPacketTiming]:
    grouped: dict[int, dict[str, TimingMarker]] = defaultdict(dict)
    raw_source_timestamps: dict[int, float] = {}
    sample_sequences: dict[int, int | None] = {}

    for marker in markers:
        key = int(round(marker.source_lsl_timestamp_seconds * 10000.0))
        grouped[key][marker.marker_name] = marker
        raw_source_timestamps[key] = marker.source_lsl_timestamp_seconds
        sample_sequences[key] = marker.sample_sequence

    packets: list[QuestPacketTiming] = []
    for key in sorted(grouped.keys()):
        marker_map = grouped[key]
        heartbeat_receive = marker_map.get("heartbeat_packet_receive")
        coherence_receive = marker_map.get("coherence_packet_receive")
        heartbeat_publish = marker_map.get("heartbeat_real_beat_publish")
        coherence_publish = marker_map.get("coherence_value_publish")
        orbit_peak = marker_map.get("orbit_radius_peak")
        packet_value = (
            heartbeat_receive.value01
            if heartbeat_receive and heartbeat_receive.value01 is not None
            else coherence_receive.value01 if coherence_receive else None
        )
        packets.append(
            QuestPacketTiming(
                source_lsl_timestamp_seconds=raw_source_timestamps[key],
                sample_sequence=sample_sequences[key],
                packet_value01=packet_value,
                heartbeat_packet_receive_at_quest_local=heartbeat_receive.quest_local_clock_seconds if heartbeat_receive else None,
                coherence_packet_receive_at_quest_local=coherence_receive.quest_local_clock_seconds if coherence_receive else None,
                heartbeat_real_beat_publish_at_quest_local=heartbeat_publish.quest_local_clock_seconds if heartbeat_publish else None,
                coherence_value_publish_at_quest_local=coherence_publish.quest_local_clock_seconds if coherence_publish else None,
                orbit_radius_peak_at_quest_local=orbit_peak.quest_local_clock_seconds if orbit_peak else None,
                orbit_radius_peak_value01=orbit_peak.value01 if orbit_peak else None,
                orbit_radius_peak_aux_value=orbit_peak.aux_value if orbit_peak else None,
            )
        )

    return packets


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


def get_clock_offset_seconds(settings: dict, clock_samples: list[ClockAlignmentSample]) -> float | None:
    direct_value = get_setting(
        settings,
        "ClockAlignmentRecommendedQuestMinusWindowsClockSeconds",
        "clockAlignmentRecommendedQuestMinusWindowsClockSeconds",
        default="",
    )
    if direct_value:
        try:
            return float(direct_value)
        except ValueError:
            pass

    candidate_samples = [sample.quest_minus_windows_clock_seconds for sample in clock_samples if sample.window_kind == "StartBurst"]
    if not candidate_samples:
        candidate_samples = [sample.quest_minus_windows_clock_seconds for sample in clock_samples]
    if not candidate_samples:
        return None
    return statistics.median(candidate_samples)


def filter_packet_overlap(
    windows_samples: list[UpstreamLslSample],
    quest_packets: list[QuestPacketTiming],
) -> tuple[list[UpstreamLslSample], list[QuestPacketTiming], float | None, float | None]:
    if not windows_samples or not quest_packets:
        return [], [], None, None

    overlap_start = max(windows_samples[0].stream_sample_timestamp_seconds, quest_packets[0].source_lsl_timestamp_seconds)
    overlap_end = min(windows_samples[-1].stream_sample_timestamp_seconds, quest_packets[-1].source_lsl_timestamp_seconds)
    if overlap_start > overlap_end:
        return [], [], None, None

    windows_overlap = [
        sample
        for sample in windows_samples
        if overlap_start <= sample.stream_sample_timestamp_seconds <= overlap_end
    ]
    quest_overlap = [
        packet
        for packet in quest_packets
        if overlap_start <= packet.source_lsl_timestamp_seconds <= overlap_end
    ]
    return windows_overlap, quest_overlap, overlap_start, overlap_end


def match_packet_timings(
    windows_samples: list[UpstreamLslSample],
    quest_packets: list[QuestPacketTiming],
    quest_minus_windows_clock_seconds: float | None,
    tolerance_seconds: float = 0.001,
) -> tuple[list[PacketTimingMatch], list[UpstreamLslSample], list[QuestPacketTiming], float | None, float | None]:
    windows_overlap, quest_overlap, overlap_start, overlap_end = filter_packet_overlap(windows_samples, quest_packets)
    if not windows_overlap or not quest_overlap or overlap_start is None:
        return [], windows_overlap, quest_overlap, overlap_start, overlap_end

    matches: list[PacketTimingMatch] = []
    windows_only: list[UpstreamLslSample] = []
    quest_only: list[QuestPacketTiming] = []
    windows_index = 0
    quest_index = 0

    while windows_index < len(windows_overlap) and quest_index < len(quest_overlap):
        windows_sample = windows_overlap[windows_index]
        quest_packet = quest_overlap[quest_index]
        delta_seconds = windows_sample.stream_sample_timestamp_seconds - quest_packet.source_lsl_timestamp_seconds

        if abs(delta_seconds) <= tolerance_seconds:
            source_timestamp = 0.5 * (
                windows_sample.stream_sample_timestamp_seconds + quest_packet.source_lsl_timestamp_seconds
            )
            relative_seconds = source_timestamp - overlap_start
            send_time_in_quest_clock = (
                source_timestamp + quest_minus_windows_clock_seconds
                if quest_minus_windows_clock_seconds is not None
                else None
            )

            def quest_latency_ms(event_time: float | None) -> float | None:
                if event_time is None or send_time_in_quest_clock is None:
                    return None
                return (event_time - send_time_in_quest_clock) * 1000.0

            def quest_pipeline_latency_ms(later_time: float | None, earlier_time: float | None) -> float | None:
                if later_time is None or earlier_time is None:
                    return None
                return (later_time - earlier_time) * 1000.0

            matches.append(
                PacketTimingMatch(
                    source_lsl_timestamp_seconds=source_timestamp,
                    relative_seconds=relative_seconds,
                    windows_sequence=windows_sample.sequence,
                    quest_sample_sequence=quest_packet.sample_sequence,
                    windows_packet_value01=windows_sample.value_numeric,
                    quest_packet_value01=quest_packet.packet_value01,
                    windows_observed_local_clock_seconds=windows_sample.observed_local_clock_seconds,
                    quest_heartbeat_packet_receive_at_quest_local=quest_packet.heartbeat_packet_receive_at_quest_local,
                    quest_coherence_value_publish_at_quest_local=quest_packet.coherence_value_publish_at_quest_local,
                    quest_orbit_radius_peak_at_quest_local=quest_packet.orbit_radius_peak_at_quest_local,
                    orbit_radius_peak_value01=quest_packet.orbit_radius_peak_value01,
                    orbit_radius_peak_aux_value=quest_packet.orbit_radius_peak_aux_value,
                    windows_receive_latency_ms=(
                        windows_sample.observed_local_clock_seconds - windows_sample.stream_sample_timestamp_seconds
                    )
                    * 1000.0,
                    quest_receive_latency_ms=quest_latency_ms(quest_packet.heartbeat_packet_receive_at_quest_local),
                    coherence_publish_latency_ms=quest_latency_ms(quest_packet.coherence_value_publish_at_quest_local),
                    orbit_peak_latency_ms=quest_latency_ms(quest_packet.orbit_radius_peak_at_quest_local),
                    quest_receive_to_orbit_peak_ms=quest_pipeline_latency_ms(
                        quest_packet.orbit_radius_peak_at_quest_local,
                        quest_packet.heartbeat_packet_receive_at_quest_local,
                    ),
                )
            )
            windows_index += 1
            quest_index += 1
        elif delta_seconds < 0.0:
            windows_only.append(windows_sample)
            windows_index += 1
        else:
            quest_only.append(quest_packet)
            quest_index += 1

    windows_only.extend(windows_overlap[windows_index:])
    quest_only.extend(quest_overlap[quest_index:])
    return matches, windows_only, quest_only, overlap_start, overlap_end


def compute_gap_series(source_timestamps: list[float], overlap_start: float) -> list[tuple[float, float]]:
    if len(source_timestamps) < 2:
        return []
    return [
        (current - overlap_start, (current - previous) * 1000.0)
        for previous, current in zip(source_timestamps, source_timestamps[1:])
    ]


def packet_scatter_points_from_windows(
    samples: list[UpstreamLslSample],
    overlap_start: float,
) -> list[tuple[float, float]]:
    return [
        (sample.stream_sample_timestamp_seconds - overlap_start, sample.value_numeric)
        for sample in samples
    ]


def packet_scatter_points_from_quest(
    packets: list[QuestPacketTiming],
    overlap_start: float,
) -> list[tuple[float, float]]:
    return [
        (packet.source_lsl_timestamp_seconds - overlap_start, packet.packet_value01)
        for packet in packets
        if packet.packet_value01 is not None
    ]


def non_null_points(values: Iterable[tuple[float, float | None]]) -> list[tuple[float, float]]:
    return [(x, y) for x, y in values if y is not None]


def analyze_packet_timings(
    settings: dict,
    windows_upstream_samples: list[UpstreamLslSample],
    quest_packet_timings: list[QuestPacketTiming],
    clock_samples: list[ClockAlignmentSample],
) -> PacketTimingAnalysis:
    quest_minus_windows_clock_seconds = get_clock_offset_seconds(settings, clock_samples)
    matches, windows_only, quest_only, overlap_start, overlap_end = match_packet_timings(
        windows_upstream_samples,
        quest_packet_timings,
        quest_minus_windows_clock_seconds,
    )
    windows_overlap, quest_overlap, _, _ = filter_packet_overlap(windows_upstream_samples, quest_packet_timings)
    return PacketTimingAnalysis(
        matches=matches,
        windows_overlap=windows_overlap,
        quest_overlap=quest_overlap,
        windows_only=windows_only,
        quest_only=quest_only,
        overlap_start=overlap_start,
        overlap_end=overlap_end,
        quest_minus_windows_clock_seconds=quest_minus_windows_clock_seconds,
    )


def write_packet_timing_csv(output_csv: Path, analysis: PacketTimingAnalysis) -> None:
    fieldnames = [
        "match_status",
        "source_lsl_timestamp_seconds",
        "seconds_since_first_overlapping_packet",
        "clock_alignment_quest_minus_windows_seconds",
        "windows_sequence",
        "quest_sample_sequence",
        "windows_packet_value01",
        "quest_packet_value01",
        "windows_observed_local_clock_seconds",
        "quest_heartbeat_packet_receive_at_quest_local",
        "quest_coherence_value_publish_at_quest_local",
        "quest_orbit_radius_peak_at_quest_local",
        "orbit_radius_peak_value01",
        "orbit_radius_peak_aux_value",
        "windows_receive_latency_ms",
        "quest_receive_latency_ms",
        "coherence_publish_latency_ms",
        "orbit_peak_latency_ms",
        "quest_receive_to_orbit_peak_ms",
    ]

    overlap_start = analysis.overlap_start
    quest_minus_windows_clock_seconds = analysis.quest_minus_windows_clock_seconds
    rows: list[dict[str, float | int | str | None]] = []

    for match in analysis.matches:
        rows.append(
            {
                "match_status": "matched",
                "source_lsl_timestamp_seconds": match.source_lsl_timestamp_seconds,
                "seconds_since_first_overlapping_packet": match.relative_seconds,
                "clock_alignment_quest_minus_windows_seconds": quest_minus_windows_clock_seconds,
                "windows_sequence": match.windows_sequence,
                "quest_sample_sequence": match.quest_sample_sequence,
                "windows_packet_value01": match.windows_packet_value01,
                "quest_packet_value01": match.quest_packet_value01,
                "windows_observed_local_clock_seconds": match.windows_observed_local_clock_seconds,
                "quest_heartbeat_packet_receive_at_quest_local": match.quest_heartbeat_packet_receive_at_quest_local,
                "quest_coherence_value_publish_at_quest_local": match.quest_coherence_value_publish_at_quest_local,
                "quest_orbit_radius_peak_at_quest_local": match.quest_orbit_radius_peak_at_quest_local,
                "orbit_radius_peak_value01": match.orbit_radius_peak_value01,
                "orbit_radius_peak_aux_value": match.orbit_radius_peak_aux_value,
                "windows_receive_latency_ms": match.windows_receive_latency_ms,
                "quest_receive_latency_ms": match.quest_receive_latency_ms,
                "coherence_publish_latency_ms": match.coherence_publish_latency_ms,
                "orbit_peak_latency_ms": match.orbit_peak_latency_ms,
                "quest_receive_to_orbit_peak_ms": match.quest_receive_to_orbit_peak_ms,
            }
        )

    for sample in analysis.windows_only:
        relative_seconds = sample.stream_sample_timestamp_seconds - overlap_start if overlap_start is not None else None
        rows.append(
            {
                "match_status": "windows_only",
                "source_lsl_timestamp_seconds": sample.stream_sample_timestamp_seconds,
                "seconds_since_first_overlapping_packet": relative_seconds,
                "clock_alignment_quest_minus_windows_seconds": quest_minus_windows_clock_seconds,
                "windows_sequence": sample.sequence,
                "quest_sample_sequence": None,
                "windows_packet_value01": sample.value_numeric,
                "quest_packet_value01": None,
                "windows_observed_local_clock_seconds": sample.observed_local_clock_seconds,
                "quest_heartbeat_packet_receive_at_quest_local": None,
                "quest_coherence_value_publish_at_quest_local": None,
                "quest_orbit_radius_peak_at_quest_local": None,
                "orbit_radius_peak_value01": None,
                "orbit_radius_peak_aux_value": None,
                "windows_receive_latency_ms": (sample.observed_local_clock_seconds - sample.stream_sample_timestamp_seconds) * 1000.0,
                "quest_receive_latency_ms": None,
                "coherence_publish_latency_ms": None,
                "orbit_peak_latency_ms": None,
                "quest_receive_to_orbit_peak_ms": None,
            }
        )

    for packet in analysis.quest_only:
        relative_seconds = packet.source_lsl_timestamp_seconds - overlap_start if overlap_start is not None else None
        send_time_in_quest_clock = (
            packet.source_lsl_timestamp_seconds + quest_minus_windows_clock_seconds
            if quest_minus_windows_clock_seconds is not None
            else None
        )

        def quest_latency_ms(event_time: float | None) -> float | None:
            if event_time is None or send_time_in_quest_clock is None:
                return None
            return (event_time - send_time_in_quest_clock) * 1000.0

        def quest_pipeline_latency_ms(later_time: float | None, earlier_time: float | None) -> float | None:
            if later_time is None or earlier_time is None:
                return None
            return (later_time - earlier_time) * 1000.0

        rows.append(
            {
                "match_status": "quest_only",
                "source_lsl_timestamp_seconds": packet.source_lsl_timestamp_seconds,
                "seconds_since_first_overlapping_packet": relative_seconds,
                "clock_alignment_quest_minus_windows_seconds": quest_minus_windows_clock_seconds,
                "windows_sequence": None,
                "quest_sample_sequence": packet.sample_sequence,
                "windows_packet_value01": None,
                "quest_packet_value01": packet.packet_value01,
                "windows_observed_local_clock_seconds": None,
                "quest_heartbeat_packet_receive_at_quest_local": packet.heartbeat_packet_receive_at_quest_local,
                "quest_coherence_value_publish_at_quest_local": packet.coherence_value_publish_at_quest_local,
                "quest_orbit_radius_peak_at_quest_local": packet.orbit_radius_peak_at_quest_local,
                "orbit_radius_peak_value01": packet.orbit_radius_peak_value01,
                "orbit_radius_peak_aux_value": packet.orbit_radius_peak_aux_value,
                "windows_receive_latency_ms": None,
                "quest_receive_latency_ms": quest_latency_ms(packet.heartbeat_packet_receive_at_quest_local),
                "coherence_publish_latency_ms": quest_latency_ms(packet.coherence_value_publish_at_quest_local),
                "orbit_peak_latency_ms": quest_latency_ms(packet.orbit_radius_peak_at_quest_local),
                "quest_receive_to_orbit_peak_ms": quest_pipeline_latency_ms(
                    packet.orbit_radius_peak_at_quest_local,
                    packet.heartbeat_packet_receive_at_quest_local,
                ),
            }
        )

    rows.sort(
        key=lambda row: (
            float(row["source_lsl_timestamp_seconds"]) if row["source_lsl_timestamp_seconds"] is not None else math.inf,
            str(row["match_status"]),
        )
    )

    output_csv.parent.mkdir(parents=True, exist_ok=True)
    with output_csv.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def plot_scatter_series(
    ax,
    points: list[tuple[float, float]],
    color: str,
    label: str,
    *,
    marker: str = "o",
    filled: bool = True,
    zorder: int = 4,
    size: float = 28,
) -> None:
    if not points:
        return
    xs = [point[0] for point in points]
    ys = [point[1] for point in points]
    ax.scatter(
        xs,
        ys,
        s=size,
        alpha=0.95,
        label=label,
        marker=marker,
        facecolors=color if filled else COLORS["panel_alt"],
        edgecolors=color,
        linewidths=1.0,
        zorder=zorder,
    )
    if len(points) > 1:
        ax.plot(xs, ys, color=color, linewidth=1.0, alpha=0.45, zorder=max(1, zorder - 1))


def summarize_series(values: list[float]) -> str:
    if not values:
        return "n/a"
    return f"{statistics.mean(values):.1f} ms mean, {statistics.median(values):.1f} ms median"


def build_packet_timing_page(
    pdf: PdfPages,
    analysis: PacketTimingAnalysis,
) -> None:
    figure, axes = plt.subplots(2, 2, figsize=PAGE_SIZE, facecolor=COLORS["page"])
    axes = axes.flatten()
    figure.subplots_adjust(left=0.07, right=0.965, top=0.79, bottom=0.11, hspace=0.34, wspace=0.20)
    figure.suptitle("Packet delivery and orbit response", color=COLORS["text"], fontsize=20, fontweight="bold", y=0.955)
    figure.text(
        0.07,
        0.875,
        "Each dot is one received heartbeat/coherence packet keyed by its source LSL timestamp, not a held twin-state mirror frame.",
        color=COLORS["muted"],
        fontsize=10.2,
    )

    matches = analysis.matches
    windows_only = analysis.windows_only
    quest_only = analysis.quest_only
    overlap_start = analysis.overlap_start
    overlap_end = analysis.overlap_end
    windows_overlap = analysis.windows_overlap
    quest_overlap = analysis.quest_overlap

    if overlap_start is None or overlap_end is None:
        for axis in axes:
            axis.set_facecolor(COLORS["panel_alt"])
            axis.axis("off")
            axis.text(0.5, 0.5, "No overlapping packet window", ha="center", va="center", color=COLORS["muted"])
        pdf.savefig(figure, facecolor=figure.get_facecolor())
        plt.close(figure)
        return

    windows_packet_points = packet_scatter_points_from_windows(windows_overlap, overlap_start)
    quest_packet_points = packet_scatter_points_from_quest(quest_overlap, overlap_start)
    windows_receive_points = non_null_points(
        (match.relative_seconds, match.windows_receive_latency_ms) for match in matches
    )
    quest_receive_points = non_null_points(
        (match.relative_seconds, match.quest_receive_latency_ms) for match in matches
    )
    coherence_publish_points = non_null_points(
        (match.relative_seconds, match.coherence_publish_latency_ms) for match in matches
    )
    orbit_peak_points = non_null_points(
        (match.relative_seconds, match.orbit_peak_latency_ms) for match in matches
    )
    quest_receive_to_peak_points = non_null_points(
        (match.relative_seconds, match.quest_receive_to_orbit_peak_ms) for match in matches
    )
    windows_gap_points = compute_gap_series(
        [sample.stream_sample_timestamp_seconds for sample in windows_overlap],
        overlap_start,
    )
    quest_gap_points = compute_gap_series(
        [packet.source_lsl_timestamp_seconds for packet in quest_overlap],
        overlap_start,
    )

    apply_axis_style(axes[0], "Packet value by send time", "unit01", "Every received packet in the shared send-time window")
    axes[0].set_xlabel("s since first overlapping packet", color=COLORS["muted"], fontsize=8.8)
    plot_scatter_series(
        axes[0],
        quest_packet_points,
        COLORS["orange"],
        f"Quest packet receive ({len(quest_packet_points)})",
        marker="o",
        filled=True,
        zorder=4,
        size=22,
    )
    plot_scatter_series(
        axes[0],
        windows_packet_points,
        COLORS["blue"],
        f"Windows upstream ({len(windows_packet_points)})",
        marker="s",
        filled=False,
        zorder=5,
        size=42,
    )
    axes[0].set_ylim(0.0, 1.0)

    apply_axis_style(axes[1], "Delivery latency after send", "ms", "Package send to receive/publish timing")
    axes[1].set_xlabel("s since first overlapping packet", color=COLORS["muted"], fontsize=8.8)
    plot_scatter_series(axes[1], windows_receive_points, COLORS["blue"], "Windows receive", marker="s", filled=False, zorder=5)
    plot_scatter_series(axes[1], quest_receive_points, COLORS["orange"], "Quest heartbeat receive", marker="o", filled=True, zorder=4)
    plot_scatter_series(axes[1], coherence_publish_points, COLORS["magenta"], "Quest coherence publish", marker="^", filled=True, zorder=4)

    apply_axis_style(axes[2], "Orbit peak latency after send", "ms", "Package send to orbit-distance peak")
    axes[2].set_xlabel("s since first overlapping packet", color=COLORS["muted"], fontsize=8.8)
    plot_scatter_series(axes[2], orbit_peak_points, COLORS["green"], "Quest orbit peak", marker="o", filled=True, zorder=4)
    plot_scatter_series(
        axes[2],
        quest_receive_to_peak_points,
        COLORS["cyan"],
        "Quest receive -> peak",
        marker="s",
        filled=False,
        zorder=5,
    )

    apply_axis_style(axes[3], "Packet gap / continuity", "ms", "Gaps between consecutive received packets")
    axes[3].set_xlabel("s since first overlapping packet", color=COLORS["muted"], fontsize=8.8)
    plot_scatter_series(axes[3], windows_gap_points, COLORS["blue"], "Windows gap", marker="s", filled=False, zorder=5)
    plot_scatter_series(axes[3], quest_gap_points, COLORS["orange"], "Quest gap", marker="o", filled=True, zorder=4)

    for axis in axes:
        if axis.has_data():
            x_max = max(
                [1.0]
                + [line.get_xdata()[-1] for line in axis.lines if len(line.get_xdata()) > 0]
                + [collection.get_offsets()[-1][0] for collection in axis.collections if len(collection.get_offsets()) > 0]
            )
            axis.set_xlim(left=0.0, right=max(1.0, float(x_max)))
            legend = axis.legend(facecolor=COLORS["panel_alt"], edgecolor=COLORS["grid"], fontsize=8.0, loc="upper right")
            for text in legend.get_texts():
                text.set_color(COLORS["text"])

    summary_text = (
        f"Overlap {overlap_end - overlap_start:.1f}s | Packets W/Q/M {len(windows_overlap)}/{len(quest_overlap)}/{len(matches)}"
        f" | Only W/Q {len(windows_only)}/{len(quest_only)}"
    )
    latency_text = "Mean latencies unavailable."
    if windows_receive_points and quest_receive_points and orbit_peak_points and quest_receive_to_peak_points:
        latency_text = (
            f"Means: W {statistics.mean([point[1] for point in windows_receive_points]):.1f} ms, "
            f"Q {statistics.mean([point[1] for point in quest_receive_points]):.1f} ms, "
            f"Q peak {statistics.mean([point[1] for point in orbit_peak_points]):.1f} ms, "
            f"Q recv->peak {statistics.mean([point[1] for point in quest_receive_to_peak_points]):.1f} ms"
        )
    figure.text(0.07, 0.052, summary_text, color=COLORS["muted"], fontsize=8.8, va="top")
    figure.text(0.07, 0.026, latency_text, color=COLORS["muted"], fontsize=8.3, va="top")
    pdf.savefig(figure, facecolor=figure.get_facecolor())
    plt.close(figure)


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
        "Review report for the operator-side session capture and any available Quest pullback.",
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


def build_report(session_dir: Path, output_pdf: Path, output_csv: Path | None = None) -> None:
    settings_path = session_dir / "session_settings.json"
    windows_signals_path = session_dir / "signals_long.csv"
    quest_signals_path = session_dir / "device-session-pull" / "signals_long.csv"
    windows_breathing_path = session_dir / "breathing_trace.csv"
    quest_breathing_path = session_dir / "device-session-pull" / "breathing_trace.csv"
    windows_events_path = session_dir / "session_events.csv"
    quest_events_path = session_dir / "device-session-pull" / "session_events.csv"
    clock_alignment_path = session_dir / "clock_alignment_roundtrip.csv"
    upstream_lsl_monitor_path = session_dir / "upstream_lsl_monitor.csv"
    timing_markers_path = session_dir / "device-session-pull" / "timing_markers.csv"

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
    windows_upstream_samples = read_upstream_lsl_samples(upstream_lsl_monitor_path)
    quest_timing_markers = read_timing_markers(timing_markers_path)
    quest_packet_timings = group_quest_packet_timings(quest_timing_markers)
    packet_timing_analysis = analyze_packet_timings(settings, windows_upstream_samples, quest_packet_timings, clock_samples)
    write_packet_timing_csv(output_csv or (session_dir / "packet_timing_analysis.csv"), packet_timing_analysis)

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
                PlotSpec("coherence.value01", "signals", "coherence.value01", "Held twin-state coherence mirror between packets"),
                PlotSpec("heartbeat.value01", "signals", "heartbeat.value01", "Normalized heartbeat envelope"),
                PlotSpec("heartbeat.packet_value01", "signals", "heartbeat.packet_value01", "Latest upstream packet value held in twin-state"),
                PlotSpec("heartbeat.real_beat_value01", "signals", "heartbeat.real_beat_value01", "Beat trigger ramp"),
            ],
        ),
        (
            "Orbit response mirrors",
            [
                PlotSpec("orbit.radius_visual01", "signals", "orbit.radius_visual01", "Runtime orbit-distance multiplier"),
                PlotSpec(
                    "orbit.radius_envelope_weight01",
                    "signals",
                    "orbit.radius_envelope_weight01",
                    "Orbit-distance envelope weight",
                ),
                PlotSpec("orbit.radius_phase01", "signals", "orbit.radius_phase01", "Orbit-distance phase"),
                PlotSpec("orbit.radius_peak_active", "signals", "orbit.radius_peak_active", "Orbit near-peak flag"),
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
        metadata["Subject"] = "Sussex session review report"
        metadata["Keywords"] = "sussex, quest, session, lsl, pdf"

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
        build_packet_timing_page(
            pdf,
            packet_timing_analysis,
        )
        build_clock_alignment_page(pdf, clock_samples, settings)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--session-dir", required=True)
    parser.add_argument("--output-pdf", required=True)
    parser.add_argument("--output-csv", required=False)
    args = parser.parse_args()

    session_dir = Path(args.session_dir).resolve()
    output_pdf = Path(args.output_pdf).resolve()
    output_csv = Path(args.output_csv).resolve() if args.output_csv else None

    build_report(session_dir, output_pdf, output_csv)
    print(str(output_pdf))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(str(exc), file=sys.stderr)
        raise
