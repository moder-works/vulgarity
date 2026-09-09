#!/usr/bin/env python3
"""A reference implementation of the fold-v1 profile, driven by data/fold-v1.json.

This mirrors TextNormalizer in the C# and Dart ports. It gives the tools a way
to fold a term without a build step, and it acts as a third opinion when the
two ports are checked against each other.
"""

import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

KIND_DROP_BREAK = 0
KIND_DROP_SILENT = 1
KIND_SOFT = 2
KIND_HARD = 3


class Folder:
    def __init__(self, path=None):
        path = path or os.path.join(ROOT, "data", "fold-v1.json")
        with open(path, encoding="utf-8") as fh:
            doc = json.load(fh)
        self.profile = doc["profile"]
        self.soft = {ord(k): v for k, v in doc["foldSoft"].items()}
        self.hard = {ord(k): v for k, v in doc["foldHard"].items()}
        self.ranges = [(r["from"], r["to"], r["base"]) for r in doc["ranges"]]
        self.min_range = min(r[0] for r in self.ranges)
        self.drop_break = [tuple(r) for r in doc["dropBreak"]]
        self.drop_silent = [tuple(r) for r in doc["dropSilent"]]

    def classify(self, cp):
        """Return (kind, text). text is None for a dropped character."""
        if 0x61 <= cp <= 0x7A:
            return KIND_HARD, chr(cp)
        if 0x41 <= cp <= 0x5A:
            return KIND_HARD, chr(cp + 32)
        if cp in self.soft:
            return KIND_SOFT, self.soft[cp]
        if cp in self.hard:
            return KIND_HARD, self.hard[cp]
        if 0x30 <= cp <= 0x39:
            return KIND_HARD, chr(cp)
        if cp >= self.min_range:
            for lo, hi, base in self.ranges:
                if lo <= cp <= hi:
                    return KIND_HARD, chr(base + (cp - lo))
        for lo, hi in self.drop_break:
            if lo <= cp <= hi:
                return KIND_DROP_BREAK, None
        for lo, hi in self.drop_silent:
            if lo <= cp <= hi:
                return KIND_DROP_SILENT, None
        return KIND_HARD, chr(cp)

    def fold(self, text):
        """Fold a string down to its normalized characters."""
        out = []
        for ch in text:
            kind, value = self.classify(ord(ch))
            if value is not None:
                out.append(value)
        return "".join(out)


def squeeze(text):
    """Collapse every run of one character down to a single character."""
    out = []
    for ch in text:
        if not out or out[-1] != ch:
            out.append(ch)
    return "".join(out)
