"""Строит размеченный корпус из статей Википедии для калибровки семантических границ.

Истина — границы разделов верхнего уровня (== Заголовок ==), размеченные авторами
статей. Сами заголовки из текста вырезаются, поэтому проверяются именно эмбеддинги,
а не структурные границы. Текст статей (CC BY-SA 4.0) в репозиторий не коммитится:
корпус генерируется в tests/calibration/wiki (каталог в .gitignore).

Запуск:
    python tests/calibration/fetch_wikipedia_corpus.py [--out DIR] [--min-section-chars N]
"""
import argparse
import json
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

API = "https://{lang}.wikipedia.org/w/api.php"
USER_AGENT = "TextChunkerCalibration/1.0 (boundary calibration; contact via repository)"

ARTICLES = {
    "ru": [
        "Кошка", "Электричество", "Москва", "Шахматы", "Байкал", "Хлеб",
        "Вакцинация", "Интернет", "Футбол", "Динозавры", "Солнечная система",
        "Лев Толстой", "Метрополитен", "Кофе", "Атом", "Парусный спорт",
        "Искусственный интеллект", "Мозг", "Вулкан", "Железная дорога",
    ],
    "en": [
        "Cat", "Electricity", "Chess", "Lake Baikal", "Bread", "Vaccination",
        "Internet", "Association football", "Dinosaur", "Solar System",
        "Coffee", "Atom", "Sailing", "Artificial intelligence", "Human brain",
        "Volcano", "Rail transport", "Photography", "Honey bee", "Tea",
    ],
}

SKIP_SECTIONS = {
    "ru": {"примечания", "литература", "ссылки", "см. также", "источники", "комментарии"},
    "en": {"references", "external links", "further reading", "see also", "notes",
           "sources", "bibliography", "footnotes", "citations"},
}

HEADING = re.compile(r"^(={2,})\s*(.+?)\s*\1\s*$")


def fetch(lang: str, title: str) -> str:
    query = urllib.parse.urlencode({
        "action": "query", "prop": "extracts", "explaintext": 1,
        "titles": title, "format": "json", "redirects": 1,
    })
    request = urllib.request.Request(
        API.format(lang=lang) + "?" + query, headers={"User-Agent": USER_AGENT})

    for attempt in range(6):
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                data = json.load(response)
            break
        except urllib.error.HTTPError as error:
            if error.code != 429 or attempt == 5:
                raise
            retry_after = error.headers.get("Retry-After")
            delay = int(retry_after) if retry_after and retry_after.isdigit() else 5 * (2 ** attempt)
            print(f"429 for {lang}:{title}, waiting {delay}s", file=sys.stderr)
            time.sleep(delay)

    page = next(iter(data["query"]["pages"].values()))
    return page.get("extract", "")


def split_sections(text: str, lang: str):
    """Список (заголовок, тело) разделов верхнего уровня; подразделы сливаются в тело."""
    sections = []
    title = None
    body = []
    skip = SKIP_SECTIONS[lang]
    for line in text.splitlines():
        match = HEADING.match(line)
        if match:
            level = len(match.group(1))
            if level == 2:
                if title is not None:
                    sections.append((title, body))
                title = match.group(2)
                body = []
            continue  # заголовки подразделов тоже вырезаем
        body.append(line)
    if title is not None:
        sections.append((title, body))

    result = []
    for name, lines in sections:
        if name.strip().lower() in skip:
            continue
        paragraphs = [p.strip() for p in "\n".join(lines).split("\n\n")]
        paragraphs = [re.sub(r"[ \t]+", " ", p) for p in paragraphs if p.strip()]
        result.append((name, paragraphs))
    return result


def build_document(lang: str, title: str, lead: str, sections, min_chars: int):
    """Склеивает разделы; возвращает (текст, список подстрок-границ)."""
    parts = []
    boundaries = []
    if lead.strip():
        parts.append(lead.strip())

    for name, paragraphs in sections:
        body = "\n\n".join(paragraphs)
        if len(body) < min_chars:
            continue
        first = paragraphs[0]
        # Подстрока из начала первого абзаца раздела: достаточно длинная, чтобы быть уникальной.
        label = first[:80].strip()
        boundaries.append(label)
        parts.append(body)

    return "\n\n".join(parts), boundaries


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default=str(Path(__file__).parent / "wiki"))
    parser.add_argument("--min-section-chars", type=int, default=400)
    parser.add_argument("--min-boundaries", type=int, default=3)
    parser.add_argument("--force", action="store_true", help="перекачать уже существующие")
    args = parser.parse_args()

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    written = 0

    for lang, titles in ARTICLES.items():
        for index, title in enumerate(titles, start=1):
            stem = f"{lang}-{index:02d}"
            if (out / f"{stem}.md").exists() and not args.force:
                print(f"{stem}: {title}: уже есть")
                continue

            try:
                text = fetch(lang, title)
            except Exception as error:  # noqa: BLE001
                print(f"skip {lang}:{title}: {error}", file=sys.stderr)
                continue

            lines = text.splitlines()
            lead_lines = []
            for line in lines:
                if HEADING.match(line):
                    break
                lead_lines.append(line)
            lead = "\n".join(lead_lines)

            sections = split_sections(text, lang)
            document, boundaries = build_document(
                lang, title, lead, sections, args.min_section_chars)

            # Метка должна встречаться ровно один раз, иначе IndexOf возьмёт чужое место.
            unique = [b for b in boundaries if document.count(b) == 1]
            if len(unique) < args.min_boundaries:
                print(f"skip {lang}:{title}: мало границ ({len(unique)})", file=sys.stderr)
                continue

            stem = f"{lang}-{index:02d}"
            (out / f"{stem}.md").write_text(document, encoding="utf-8", newline="\n")
            (out / f"{stem}.json").write_text(
                json.dumps({"title": title, "boundaries": unique}, ensure_ascii=False, indent=2),
                encoding="utf-8")
            written += 1
            print(f"{stem}: {title}: {len(document)} chars, {len(unique)} boundaries")
            time.sleep(0.3)

    (out / "ATTRIBUTION.txt").write_text(
        "Тексты получены из Википедии (CC BY-SA 4.0): https://creativecommons.org/licenses/by-sa/4.0/\n"
        "Авторы и история правок — на страницах соответствующих статей.\n",
        encoding="utf-8")
    print(f"written: {written}")
    return 0 if written else 1


if __name__ == "__main__":
    sys.exit(main())
