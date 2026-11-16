"""Small rendered fixtures exercise layout decisions without a GUI application."""
import importlib.util
import tempfile
import unittest
from pathlib import Path

import pymupdf

spec = importlib.util.spec_from_file_location("audit_layout", Path(__file__).parents[2] / "src/Thesis.Cli/scripts/audit_layout.py")
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class LayoutAuditTests(unittest.TestCase):
    def document(self, pages, headings, formats):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "fixture.pdf"
            with pymupdf.open() as pdf:
                for lines in pages:
                    page = pdf.new_page(width=595, height=842)
                    for x, y, text in lines:
                        page.insert_text((x, y), text)
                pdf.save(path)
            return audit.analyze(path, {"headings": headings, "pageFormats": formats})

    def test_section_numbering_disambiguates_short_table_text(self):
        facts = self.document([
            [(70, 100, "Abstract"), (70, 150, "Body text."), (290, 810, "I")],
            [(70, 100, "Abstract"), (70, 150, "Table cell."), (290, 810, "1")],
        ], ["Abstract"], {"Abstract": "upperRoman"})
        self.assertEqual([], facts["errors"])
        self.assertEqual({"Abstract": "I"}, facts["tocPages"])

    def test_text_on_normal_pages_is_checked_even_with_sentences(self):
        facts = self.document([
            [(70, 100, "Chapter one"), (70, 150, "One... then two."), (70, 180, "More... text."), (290, 810, "1")],
        ], ["Chapter one"], {"Chapter one": "decimal"})
        self.assertEqual([], facts["errors"])
        self.assertEqual("1", facts["tocPages"]["Chapter one"])

    def test_ambiguous_heading_fails(self):
        facts = self.document([
            [(70, 100, "Heading"), (290, 810, "1")],
            [(70, 100, "Heading"), (290, 810, "2")],
        ], ["Heading"], {"Heading": "decimal"})
        self.assertTrue(any("2 matches" in error for error in facts["errors"]))

    def test_orphan_heading_fails(self):
        facts = self.document([[(70, 680, "Heading"), (290, 810, "1")]], ["Heading"], {"Heading": "decimal"})
        self.assertTrue(any("no following body" in error for error in facts["errors"]))

    def test_missing_page_label_fails(self):
        facts = self.document([[(70, 100, "Heading"), (70, 150, "Body")]], ["Heading"], {"Heading": "decimal"})
        self.assertTrue(any("label is missing" in error for error in facts["errors"]))

    def test_bottom_table_number_is_not_a_footer_label(self):
        facts = self.document([[(70, 100, "Heading"), (70, 150, "Body"), (150, 730, "41"), (290, 810, "17")]],
                              ["Heading"], {"Heading": "decimal"})
        self.assertEqual([], facts["errors"])
        self.assertEqual("17", facts["tocPages"]["Heading"])


if __name__ == "__main__":
    unittest.main()
