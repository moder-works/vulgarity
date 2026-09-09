#!/usr/bin/env python3
"""Checks packlib against the shared hostile-pack fixtures.

The Dart and C# readers read the same file, so a pack one reader accepts and
another refuses fails here as well as there.

Run: python3 -m unittest discover -s tool -p 'test_*.py'
"""

import base64
import json
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import packlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FIXTURES = os.path.join(ROOT, "data", "testdata", "hostile-packs.json")


def load_fixtures():
    with open(FIXTURES, encoding="utf-8") as fh:
        return json.load(fh)


class HostilePackTests(unittest.TestCase):
    """Every case in data/testdata/hostile-packs.json."""

    @classmethod
    def setUpClass(cls):
        cls.doc = load_fixtures()

    def test_every_case(self):
        for case in self.doc["cases"]:
            with self.subTest(case["name"]):
                pack = base64.b64decode(case["pack"])

                if case["expect"] == "error":
                    with self.assertRaises(packlib.PackError):
                        packlib.decode(pack, expected_profile=self.doc["profile"])
                    continue

                doc = packlib.decode(pack, expected_profile=self.doc["profile"])
                self.assertEqual(case["terms"], len(doc["entries"]))

    def test_the_fixtures_cover_both_outcomes(self):
        outcomes = {c["expect"] for c in self.doc["cases"]}
        self.assertEqual({"error", "loads"}, outcomes)

    def test_every_loading_case_states_a_term_count(self):
        for case in self.doc["cases"]:
            if case["expect"] == "loads":
                self.assertIn("terms", case, case["name"])


class WriterTests(unittest.TestCase):
    """What packlib.encode refuses to write."""

    def test_the_writer_refuses_more_than_max_categories(self):
        # A category index is 4 bits, so a 17th name could never be reached.
        # The writer is the only place that can catch this, because a reader
        # sees nothing wrong with the bytes.
        doc = {
            "profile": "fold-v1",
            "entries": [
                {"t": "term%02d" % i, "cat": "cat%02d" % i, "sev": 3}
                for i in range(packlib.MAX_CATEGORIES + 1)
            ],
        }

        with self.assertRaises(packlib.PackError) as caught:
            packlib.encode(doc)
        self.assertIn("at most %d categories" % packlib.MAX_CATEGORIES,
                      str(caught.exception))

    def test_the_writer_accepts_exactly_max_categories(self):
        doc = {
            "profile": "fold-v1",
            "entries": [
                {"t": "term%02d" % i, "cat": "cat%02d" % i, "sev": 3}
                for i in range(packlib.MAX_CATEGORIES)
            ],
        }

        back = packlib.decode(packlib.encode(doc), expected_profile="fold-v1")
        self.assertEqual(packlib.MAX_CATEGORIES, len(back["entries"]))

    def test_the_writer_refuses_an_empty_term(self):
        doc = {"profile": "fold-v1", "entries": [{"t": "", "sev": 3}]}
        with self.assertRaises(packlib.PackError):
            packlib.encode(doc)

    def test_the_writer_refuses_a_severity_out_of_range(self):
        for severity in (0, 6):
            doc = {"profile": "fold-v1",
                   "entries": [{"t": "blorp", "sev": severity}]}
            with self.assertRaises(packlib.PackError):
                packlib.encode(doc)

    def test_the_writer_refuses_a_varint_wider_than_the_cap(self):
        out = bytearray()
        with self.assertRaises(packlib.PackError):
            packlib.put_varint(out, packlib.MAX_VARINT + 1)


class SeedRoundTripTests(unittest.TestCase):
    """The bundled English list survives a round trip unchanged."""

    def test_the_english_seed_round_trips(self):
        with open(os.path.join(ROOT, "data", "seed.json"), encoding="utf-8") as fh:
            doc = json.load(fh)

        back = packlib.decode(packlib.encode(doc), expected_profile=doc["profile"])

        self.assertEqual(len(doc["entries"]), len(back["entries"]))
        self.assertEqual([e["t"] for e in doc["entries"]],
                         [e["t"] for e in back["entries"]])
        self.assertEqual(sorted(w for w in doc.get("allow", []) if w),
                         sorted(back.get("allow", [])))

    def test_the_profile_is_pinned(self):
        with open(os.path.join(ROOT, "data", "seed.json"), encoding="utf-8") as fh:
            doc = json.load(fh)

        with self.assertRaises(packlib.PackError):
            packlib.decode(packlib.encode(doc), expected_profile="fold-v0")


if __name__ == "__main__":
    unittest.main()
