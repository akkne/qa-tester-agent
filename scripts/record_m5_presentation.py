#!/usr/bin/env python3
"""Render a short, silent M5 evidence presentation from check_m5_cycle.sh output."""

import json
import subprocess
import sys
from pathlib import Path


def main() -> int:
    evidence = Path(sys.argv[1] if len(sys.argv) > 1 else "/tmp/qa-agent-m5-cycle-evidence").resolve()
    summary = json.loads((evidence / "summary.json").read_text())
    font = Path("/System/Library/Fonts/Supplemental/Arial Unicode.ttf")
    if not font.is_file():
        raise RuntimeError("The presentation font is unavailable on this Mac.")

    slides = [
        ("QA Agent · М5", "Два последовательных обновления\nЛокальная приёмка на отдельных Git SHA"),
        ("Первый push: дефект доступа", f"{summary['firstHeadSha'][:12]}\n"
         f"Полный прогон: {summary['first']['failedTests']} упавших тестов из {summary['first']['totalTests']}\n"
         "Chromium desktop и mobile: ошибка подтверждена"),
        ("QA-ветка и принятие теста", f"{summary['qaBranch']}\n"
         "Тест-кандидат проверяет запрет доступа к чужому кабинету.\n"
         "После принятия тест падает на старом продукте."),
        ("Второй push: исправление", f"{summary['secondHeadSha'][:12]}\n"
         f"Вся база: {summary['second']['totalTests']} тестов, {summary['second']['failedTests']} ошибок\n"
         "Chromium desktop и mobile: пройдены"),
        ("Стабилизация", "Повтор события возвращает прежний запуск.\n"
         "QA-ветки исключены из запуска.\n"
         "Временные сбои LLM повторяются с пределом попыток."),
    ]
    filters = []
    for index, (title, body) in enumerate(slides):
        title_file = evidence / f"slide-{index}-title.txt"
        body_file = evidence / f"slide-{index}-body.txt"
        title_file.write_text(title)
        body_file.write_text(body)
        start, end = index * 8, (index + 1) * 8
        common = f"fontfile='{font}':fontcolor=white:enable='between(t,{start},{end})'"
        filters.append(f"drawtext={common}:textfile='{title_file}':fontsize=52:x=90:y=110")
        filters.append(f"drawtext={common}:textfile='{body_file}':fontsize=31:line_spacing=22:x=90:y=240")

    output = evidence / "m5-presentation.mp4"
    subprocess.run(
        ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
         "color=c=0x17243b:s=1280x720:r=24:d=40", "-vf", ",".join(filters),
         "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", str(output)],
        check=True,
    )
    print(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
