"""Exercise the real CLI -> DOCX -> PDF -> audit pipeline without private theses."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import zipfile

import pymupdf


def create_docx(path, cover=False):
    w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
    r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
    p = lambda text: f"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>"
    body = p("1 Introduction") + p("The original authored text must survive template application.")
    if cover:
        body = "<w:p><w:pPr><w:outlineLvl w:val='0'/></w:pPr><w:r><w:t>Template cover title</w:t></w:r></w:p>" + body
    body += "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w='4000'/><w:gridCol w:w='4000'/></w:tblGrid><w:tr>"
    for text in ("Measured value", "42"):
        body += f"<w:tc><w:tcPr><w:tcW w:w='4000' w:type='dxa'/></w:tcPr>{p(text)}</w:tc>"
    body += "</w:tr></w:tbl>"
    body += "<w:sectPr><w:footerReference w:type='default' r:id='footer'/><w:pgSz w:w='11906' w:h='16838'/><w:pgMar w:top='1440' w:right='1440' w:bottom='1440' w:left='1440' w:header='720' w:footer='720'/><w:pgNumType w:fmt='decimal' w:start='1'/></w:sectPr>"
    parts = {
        "[Content_Types].xml": "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='xml' ContentType='application/xml'/><Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/><Override PartName='/word/footer.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml'/></Types>",
        "_rels/.rels": "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='main' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/></Relationships>",
        "word/_rels/document.xml.rels": f"<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='footer' Type='{r}/footer' Target='footer.xml'/></Relationships>",
        "word/document.xml": f"<w:document xmlns:w='{w}' xmlns:r='{r}'><w:body>{body}</w:body></w:document>",
        "word/footer.xml": f"<w:ftr xmlns:w='{w}'><w:p><w:pPr><w:jc w:val='center'/></w:pPr><w:fldSimple w:instr=' PAGE '><w:r><w:t>1</w:t></w:r></w:fldSimple></w:p></w:ftr>",
    }
    with zipfile.ZipFile(path, "w") as archive:
        for name, value in parts.items():
            archive.writestr(name, value)


def run(cli, office, engine, artifact_dir):
    artifact_dir.mkdir(parents=True, exist_ok=True)
    source = artifact_dir / "source.docx"
    template = artifact_dir / "template.docx"
    create_docx(source)
    create_docx(template, cover=True)
    fingerprint = hashlib.sha256(source.read_bytes()).hexdigest()
    roles = {name: {"latinFont": "Liberation Serif"} for name in ("body", "table", "image", "toc1")}
    roles["heading1"] = {"latinFont": "Liberation Serif", "outlineLevel": 0, "keepNext": True}
    plan = {"sourceSha256": fingerprint, "templateSha256": hashlib.sha256(template.read_bytes()).hexdigest(), "requireSourceCoverage": True, "roles": roles,
            "blocks": [{"type": "templateRange", "start": 0, "end": 1}, {"type": "toc"}, {"type": "sourceRange", "start": 0, "end": 1, "role": "heading1"},
                       {"type": "sourceRange", "start": 1, "end": 3, "role": "body"}, {"type": "section", "finalSection": True}]}
    (artifact_dir / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
    config = {"engine": engine, "libreOfficePath": str(office), "timeoutSeconds": 90, "maxPasses": 4}
    (artifact_dir / "render.json").write_text(json.dumps(config), encoding="utf-8")
    command = ["dotnet", str(cli), "compose", "--doc", str(source), "--template", str(template),
               "--plan", str(artifact_dir / "plan.json"), "--render-config", str(artifact_dir / "render.json"),
               "--out", str(artifact_dir / "candidate.docx"), "--pdf", str(artifact_dir / "candidate.pdf"),
               "--report", str(artifact_dir / "report.json"), "--workdir", str(artifact_dir / "work"), "--require-ready", "false"]
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", timeout=420)
    (artifact_dir / "cli-result.json").write_text(result.stdout + result.stderr, encoding="utf-8")
    assert result.returncode == 0, result.stdout + result.stderr
    report = json.loads((artifact_dir / "report.json").read_text(encoding="utf-8"))
    assert report["automatedChecksPassed"], report
    assert report["renderPasses"] >= 2, report
    assert not report["ready"] and report["pendingReviews"], report
    assert hashlib.sha256(source.read_bytes()).hexdigest() == fingerprint
    with pymupdf.open(artifact_dir / "candidate.pdf") as pdf:
        text = " ".join(page.get_text() for page in pdf)
        assert "authored text must survive" in text and "Measured value" in text and "42" in text, text
        assert text.count("Template cover title") == 1, "Cover outline leaked into the table of contents: " + text
        assert len(pdf) == report["renderedPages"]
    with zipfile.ZipFile(artifact_dir / "candidate.docx") as docx:
        xml = docx.read("word/document.xml").decode()
        assert "PAGEREF thesis_heading_0" in xml or engine != "libreoffice", xml
        assert "authored text must survive" in xml
    print(f"Real {engine} compose passed: {report['renderedPages']} pages, {report['renderPasses']} passes.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--cli", type=Path, required=True)
    parser.add_argument("--libreoffice", type=Path, default=Path("/usr/bin/soffice"))
    parser.add_argument("--engine", choices=("libreoffice", "wps", "word"), default="libreoffice")
    parser.add_argument("--artifacts", type=Path)
    args = parser.parse_args()
    if args.artifacts:
        run(args.cli.resolve(), args.libreoffice.resolve(), args.engine, args.artifacts.resolve())
    else:
        with tempfile.TemporaryDirectory(prefix="thesis-integration-") as directory:
            run(args.cli.resolve(), args.libreoffice.resolve(), args.engine, Path(directory))
