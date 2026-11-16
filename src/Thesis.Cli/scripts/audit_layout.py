"""Read actual PDF positions and page labels. Requires PyMuPDF; never edits inputs."""
import json
import re
import sys
import unicodedata
from pathlib import Path

import pymupdf


def compact(text):
    return re.sub(r"\s+", "", unicodedata.normalize("NFKC", text))


def labels_for(labels, fmt):
    pattern = r"[0-9]+" if fmt == "decimal" else r"[IVXLCDM]+" if fmt == "upperRoman" else r"[ivxlcdm]+"
    return [label for label in labels if re.fullmatch(pattern, label)]


def analyze(pdf_path, manifest):
    errors, pages = [], []
    with pymupdf.open(pdf_path) as pdf:
        for number, page in enumerate(pdf):
            lines = []
            for block in page.get_text("dict")["blocks"]:
                for line in block.get("lines", []):
                    text = "".join(span["text"] for span in line["spans"]).strip()
                    if text:
                        lines.append((text, line["bbox"]))
            numeric_lines = [(text, box) for text, box in lines if box[1] > page.rect.height * .85
                             and re.fullmatch(r"[0-9]+|[IVXLCDMivxlcdm]+", text)]
            # Table rows can extend into the lower page band. Footer labels are
            # taken from the lowest numeric baseline, not every number in that band.
            lowest = max((box[1] for _, box in numeric_lines), default=0)
            labels = [text for text, box in numeric_lines if box[1] >= lowest - 3]
            pages.append((lines, labels, page.rect.height))
            for text, box in lines:
                if box[0] < -1 or box[1] < -1 or box[2] > page.rect.width + 1 or box[3] > page.rect.height + 1:
                    errors.append(f"Page {number + 1}: text extends outside the page: {text[:60]}")
            for image in page.get_image_info():
                box = image["bbox"]
                if box[0] < -1 or box[1] < -1 or box[2] > page.rect.width + 1 or box[3] > page.rect.height + 1:
                    errors.append(f"Page {number + 1}: image extends outside the page.")

        outlines = {}
        for _, title, page in pdf.get_toc():
            outlines.setdefault(compact(title), set()).add(page - 1)
        toc_page_indexes = {i for i, (lines, _, _) in enumerate(pages)
                            if any(compact(text).lower() in ("目录", "contents", "tableofcontents") for text, _ in lines)
                            or sum(bool(re.search(r"[.．·]{3,}\s*(?:[0-9]+|[IVXLCDMivxlcdm]+)\s*$", text)) for text, _ in lines) >= 2}
        mapping = {}
        headings = manifest["headings"]
        heading_keys = {compact(h) for h in headings}
        if len(headings) != len(heading_keys):
            errors.append("Repeated heading text requires distinct headings for TOC page-cache verification.")
        for heading in headings:
            key = compact(heading)
            fmt = manifest["pageFormats"].get(heading, "decimal")
            matches, positions = set(), {}
            for index, (lines, _, height) in enumerate(pages):
                if index in toc_page_indexes:
                    continue
                for i, (text, box) in enumerate(lines):
                    if compact(text) == key and box[1] < height * .9:
                        matches.add(index)
                        positions[index] = box
                    elif key.startswith(compact(text)):
                        for count in (2, 3):
                            segment = lines[i:i + count]
                            if len(segment) == count and compact("".join(t for t, _ in segment)) == key:
                                matches.add(index)
                                positions[index] = segment[-1][1]
            # A short heading may also occur in a table. A declared section page
            # numbering format disambiguates Roman front matter from Arabic body pages.
            explicit = outlines.get(key, set())
            if len(explicit) == 1:
                matches = explicit
            elif len(matches) > 1:
                matches = {i for i in matches if len(labels_for(pages[i][1], fmt)) == 1}
            if len(matches) != 1:
                errors.append(f"Heading must resolve to one rendered page: {heading} ({len(matches)} matches)")
                continue
            index = matches.pop()
            lines, labels, height = pages[index]
            valid = labels_for(labels, fmt)
            if len(valid) != 1:
                errors.append(f"Heading page label is missing or ambiguous: {heading}, PDF page {index + 1}")
            else:
                mapping[heading] = valid[0]
            box = positions.get(index)
            if box and box[3] > height * .6 and not any(
                    y[1] >= box[3] - 1 and y[3] < height * .88 and compact(text) not in heading_keys
                    for text, y in lines):
                errors.append(f"Heading has no following body content on PDF page {index + 1}: {heading}")
        return {"pageCount": len(pdf), "tocPages": mapping, "errors": sorted(set(errors)), "reviewItems": [
            "Review table continuations, repeated table headers and caption placement in the exported PDF.",
            "Review font substitution, formula layout, cover alignment and page balance visually.",
            "This report checks rendered pages and TOC labels; scholarly content and references need separate review.",
        ]}


if __name__ == "__main__":
    pdf_path, manifest_path, output_path = sys.argv[1:]
    manifest = json.loads(Path(manifest_path).read_text(encoding="utf-8-sig"))
    Path(output_path).write_text(json.dumps(analyze(pdf_path, manifest), ensure_ascii=False, indent=2), encoding="utf-8")
