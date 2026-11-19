#!/usr/bin/env python3
"""Проверяет языковые версии, локальные ссылки и иллюстрации документации."""

from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import unquote

FENCES = re.compile(r"^ {0,3}(?:```|~~~)[^\n]*\n.*?^ {0,3}(?:```|~~~)[^\n]*(?:\n|$)", re.M | re.S)
LINKS = re.compile(r"\]\(([^\n)]+)\)")
ANCHORS = re.compile(r'<a\s+(?:name|id)=["\']([^"\']+)["\']\s*>', re.I)
LANGUAGES = ((".md", "English"), (".ru.md", "Русский"), (".zh-CN.md", "简体中文"))


def without_code(text):
    return FENCES.sub("", text)


def heading_ids(text):
    text = without_code(text)
    counts = {}
    result = set(ANCHORS.findall(text))
    for title in re.findall(r"^#{1,6}\s+(.+?)(?:\s+#+)?$", text, re.M):
        title = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", title)
        title = re.sub(r"<[^>]+>", "", title).strip().lower()
        slug = re.sub(r"[^\w\- ]", "", title).replace(" ", "-")
        count = counts.get(slug, 0)
        counts[slug] = count + 1
        result.add(slug if count == 0 else f"{slug}-{count}")
    return result


def language_suffix(name):
    return next(suffix for suffix, _ in reversed(LANGUAGES) if name.endswith(suffix))


def is_project_document(name):
    return (name.endswith(".md") and not name.endswith("AGENTS.md")
            and not name.startswith(".github/") and "/wwwroot/lib/" not in name)


def validate(root, names):
    root = root.resolve()
    documents = [name for name in names if is_project_document(name)]
    tracked = set(names)
    directories = {parent.as_posix() for name in names for parent in Path(name).parents}
    texts = {}
    errors = []
    links = 0
    for name in documents:
        try:
            texts[name] = (root / name).read_text(encoding="utf-8-sig")
        except (OSError, UnicodeError) as error:
            errors.append(f"{name}: cannot read UTF-8: {error}")
    anchors = {name: heading_ids(text) for name, text in texts.items()}
    for name, text in texts.items():
        path = Path(name)
        language = language_suffix(name)
        base = name.removesuffix(language)
        partners = {base + suffix: label for suffix, label in LANGUAGES}
        for partner in partners.keys() - {name}:
            if partner not in texts:
                errors.append(f"{name}: missing language counterpart {partner}")
        expected = " | ".join(f"**{label}**" if partner == name else f"[{label}]({Path(partner).name})"
                              for partner, label in partners.items())
        if expected not in text:
            errors.append(f"{name}: missing language switch")
        for match in LINKS.finditer(without_code(text)):
            target = match.group(1)
            if re.match(r"^(?:[a-zA-Z][\w+.-]*:|//)", target):
                continue
            target = target.split(' "', 1)[0]
            relative, separator, fragment = target.partition("#")
            destination = (root / path.parent / unquote(relative)).resolve() if relative else root / name
            try:
                destination_name = destination.relative_to(root).as_posix()
            except ValueError:
                errors.append(f"{name}: link leaves repository: {target}")
                continue
            links += 1
            if destination_name not in tracked and destination_name not in directories:
                errors.append(f"{name}: missing or incorrectly cased path: {target}")
                continue
            if separator and destination_name.endswith(".md"):
                ids = anchors.get(destination_name)
                if ids is None:
                    ids = heading_ids(destination.read_text(encoding="utf-8-sig"))
                if unquote(fragment) not in ids:
                    errors.append(f"{name}: missing heading: {target}")
            # Переключатель языка ведёт на соседнюю версию того же документа.
            switch = destination_name in partners and not separator
            if destination_name in texts and not switch and language_suffix(destination_name) != language:
                errors.append(f"{name}: cross-language navigation: {target}")
        if "\ufffd" in text:
            errors.append(f"{name}: replacement character in UTF-8 text")
    return errors, len(texts), links


def main():
    root = Path(__file__).resolve().parent.parent
    result = subprocess.run(["git", "ls-files", "-z"], cwd=root, capture_output=True, check=True)
    names = [name for name in result.stdout.decode("utf-8").split("\0") if name]
    errors, documents, links = validate(root, names)
    for error in errors:
        print(error, file=sys.stderr)
    print(f"Documentation: {documents} documents, {links} local links, {len(errors)} errors.")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
