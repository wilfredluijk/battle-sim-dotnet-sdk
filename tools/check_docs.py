"""Check relative Markdown links without third-party dependencies."""
from pathlib import Path
import re
from urllib.parse import unquote

root = Path(__file__).resolve().parents[1]
failures = []
for path in root.rglob("*.md"):
    if any(part in {"bin", "obj", ".git"} for part in path.parts):
        continue
    text = re.sub(r"```.*?```", "", path.read_text(), flags=re.S)
    for href in re.findall(r"\]\(([^)]+)\)", text):
        if href.startswith(("http:", "https:", "mailto:", "#")):
            continue
        target = unquote(href.split("#", 1)[0])
        if not (path.parent / target).exists():
            failures.append(f"{path.relative_to(root)}: {target}")
if failures:
    raise SystemExit("Broken local links:\n" + "\n".join(failures))
print("All local Markdown links resolve.")
