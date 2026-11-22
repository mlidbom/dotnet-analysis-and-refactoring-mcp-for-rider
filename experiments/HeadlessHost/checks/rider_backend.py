import re

from .engine import RIDER_PORT


def check_document(engine, path):
    """A named file is compared to disk by the checkout's own Rider before its declarations are read."""
    answer = engine.call("list_symbols_in_file", port=RIDER_PORT, filePath=str(path))
    present = bool(re.search(r"\b[1-9]\d* (?:symbols|declarations)\b", answer))
    matches = present and "differs from disk" not in answer and "no project yet" not in answer
    engine.observe("Rider backend document matches disk", matches, path=str(path))
    return matches


def require_document(engine, path):
    if not check_document(engine, path):
        raise RuntimeError(f"The open Rider solution did not resolve the prepared file: {path}")
