internal static partial class Program
{
    static void WriteFixtureDocx(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddZipEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
              <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
              <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
            </Types>
            """);
        AddZipEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdHeader1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rIdNumbering" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/styles.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style>
            </w:styles>
            """);
        AddZipEntry(
            archive,
            "word/numbering.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="0">
                <w:lvl w:ilvl="0"><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl>
              </w:abstractNum>
              <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
            </w:numbering>
            """);
        AddZipEntry(
            archive,
            "word/header1.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>页眉</w:t></w:r></w:p></w:hdr>
            """);
        AddZipEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p><w:pPr><w:pStyle w:val="Title"/></w:pPr><w:r><w:t>中文摘要</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
                <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>列表项</w:t></w:r></w:p>
                <w:tbl>
                  <w:tr><w:tc><w:p><w:r><w:t>A1</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B1</w:t></w:r></w:p></w:tc></w:tr>
                  <w:tr><w:tc><w:p><w:r><w:t>A2</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B2</w:t></w:r></w:p></w:tc></w:tr>
                </w:tbl>
                <w:p><w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText>TOC \o "1-3" \h \z \u</w:instrText></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>摘要</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Abstract</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>目录</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>参考文献</w:t></w:r></w:p>
                <w:sectPr>
                  <w:headerReference w:type="default" r:id="rIdHeader1"/>
                  <w:pgSz w:w="11906" w:h="16838"/>
                  <w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800" w:header="720" w:footer="720" w:gutter="0"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """);
    }

    static void WriteFormattedFixtureDocx(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddZipEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
              <Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
              <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
            </Types>
            """);
        AddZipEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdHeader1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rIdNumbering" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/styles.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/></w:style>
            </w:styles>
            """);
        AddZipEntry(
            archive,
            "word/numbering.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="0">
                <w:lvl w:ilvl="0"><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl>
              </w:abstractNum>
              <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
            </w:numbering>
            """);
        AddZipEntry(
            archive,
            "word/header1.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>页眉</w:t></w:r></w:p></w:hdr>
            """);
        AddZipEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p>
                  <w:pPr>
                    <w:pStyle w:val="Heading1"/>
                    <w:jc w:val="center"/>
                    <w:spacing w:before="240" w:after="120" w:line="360" w:lineRule="auto"/>
                    <w:ind w:firstLine="480" w:left="240" w:right="120"/>
                  </w:pPr>
                  <w:r>
                    <w:rPr>
                      <w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman" w:eastAsia="宋体" w:cs="Times New Roman"/>
                      <w:b/>
                      <w:sz w:val="28"/>
                    </w:rPr>
                    <w:t>摘要</w:t>
                  </w:r>
                </w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/><w:outlineLvl w:val="0"/></w:pPr><w:r><w:rPr><w:b w:val="false"/></w:rPr><w:t>第一章 绪论</w:t></w:r></w:p>
                <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>列表项</w:t></w:r></w:p>
                <w:tbl>
                  <w:tblPr>
                    <w:tblW w:w="8640" w:type="dxa"/>
                    <w:jc w:val="center"/>
                    <w:tblBorders>
                      <w:top w:val="single" w:sz="12" w:color="000000"/>
                      <w:bottom w:val="single" w:sz="12" w:color="000000"/>
                      <w:left w:val="nil"/>
                      <w:right w:val="nil"/>
                      <w:insideH w:val="single" w:sz="4" w:color="000000"/>
                      <w:insideV w:val="nil"/>
                    </w:tblBorders>
                    <w:tblCellMar>
                      <w:top w:w="60" w:type="dxa"/>
                      <w:left w:w="120" w:type="dxa"/>
                      <w:bottom w:w="60" w:type="dxa"/>
                      <w:right w:w="120" w:type="dxa"/>
                    </w:tblCellMar>
                  </w:tblPr>
                  <w:tblGrid>
                    <w:gridCol w:w="4320"/>
                    <w:gridCol w:w="4320"/>
                  </w:tblGrid>
                  <w:tr>
                    <w:trPr><w:tblHeader/></w:trPr>
                    <w:tc>
                      <w:p>
                        <w:pPr><w:jc w:val="center"/></w:pPr>
                        <w:r>
                          <w:rPr>
                            <w:rFonts w:eastAsia="宋体"/>
                            <w:b/>
                            <w:sz w:val="21"/>
                          </w:rPr>
                          <w:t>A1</w:t>
                        </w:r>
                      </w:p>
                    </w:tc>
                    <w:tc><w:p><w:r><w:t>B1</w:t></w:r></w:p></w:tc>
                  </w:tr>
                  <w:tr><w:tc><w:p><w:r><w:t>A2</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>B2</w:t></w:r></w:p></w:tc></w:tr>
                </w:tbl>
                <w:p><w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText>TOC \o "1-3" \h \z \u</w:instrText></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>摘要</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Abstract</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>目录</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>参考文献</w:t></w:r></w:p>
                <w:sectPr>
                  <w:headerReference w:type="default" r:id="rIdHeader1"/>
                  <w:pgSz w:w="11906" w:h="16838"/>
                  <w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800" w:header="720" w:footer="720" w:gutter="0"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """);
    }

    static void WriteMultiSectionThesisTemplateDocx(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddZipEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
              <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
              <Override PartName="/word/header2.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
              <Override PartName="/word/header3.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
            </Types>
            """);
        AddZipEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rIdCoverHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
              <Relationship Id="rIdBodyHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header2.xml"/>
              <Relationship Id="rIdTailHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header3.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/styles.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/></w:style>
            </w:styles>
            """);
        AddZipEntry(
            archive,
            "word/header1.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>封面页眉</w:t></w:r></w:p></w:hdr>
            """);
        AddZipEntry(
            archive,
            "word/header2.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>正文页眉</w:t></w:r></w:p></w:hdr>
            """);
        AddZipEntry(
            archive,
            "word/header3.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>尾部页眉</w:t></w:r></w:p></w:hdr>
            """);
        AddZipEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p><w:r><w:t>封面保留</w:t></w:r></w:p>
                <w:p>
                  <w:pPr>
                    <w:sectPr>
                      <w:headerReference w:type="default" r:id="rIdCoverHeader"/>
                      <w:pgSz w:w="11906" w:h="16838"/>
                      <w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800" w:header="720" w:footer="720" w:gutter="0"/>
                    </w:sectPr>
                  </w:pPr>
                </w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>摘要</w:t></w:r></w:p>
                <w:p><w:r><w:t>模板摘要占位</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>目录</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
                <w:p><w:r><w:t>模板正文占位</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>参考文献</w:t></w:r></w:p>
                <w:p>
                  <w:pPr>
                    <w:sectPr>
                      <w:headerReference w:type="default" r:id="rIdBodyHeader"/>
                      <w:pgSz w:w="11906" w:h="16838"/>
                      <w:pgMar w:top="1134" w:right="1134" w:bottom="1134" w:left="1701" w:header="720" w:footer="720" w:gutter="0"/>
                    </w:sectPr>
                  </w:pPr>
                </w:p>
                <w:p><w:r><w:t>格式说明保留</w:t></w:r></w:p>
                <w:sectPr>
                  <w:headerReference w:type="default" r:id="rIdTailHeader"/>
                  <w:pgSz w:w="11906" w:h="16838"/>
                  <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """);
    }

    static void WriteMultiSectionTemplateWithoutTailSectionDocx(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddZipEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
              <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
              <Override PartName="/word/header2.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
              <Override PartName="/word/header3.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
            </Types>
            """);
        AddZipEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rIdCoverHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
              <Relationship Id="rIdTocHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header2.xml"/>
              <Relationship Id="rIdBodyHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header3.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/styles.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/></w:style>
            </w:styles>
            """);
        AddZipEntry(
            archive,
            "word/header1.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>封面页眉</w:t></w:r></w:p></w:hdr>
            """);
        AddZipEntry(
            archive,
            "word/header2.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>目录页眉</w:t></w:r></w:p></w:hdr>
            """);
        AddZipEntry(
            archive,
            "word/header3.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>正文页眉</w:t></w:r></w:p></w:hdr>
            """);
        AddZipEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                <w:p><w:r><w:t>封面保留</w:t></w:r></w:p>
                <w:p>
                  <w:pPr>
                    <w:sectPr>
                      <w:headerReference w:type="default" r:id="rIdCoverHeader"/>
                      <w:pgSz w:w="11906" w:h="16838"/>
                      <w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800" w:header="720" w:footer="720" w:gutter="0"/>
                    </w:sectPr>
                  </w:pPr>
                </w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>摘要</w:t></w:r></w:p>
                <w:p><w:r><w:t>模板摘要占位</w:t></w:r></w:p>
                <w:p>
                  <w:pPr>
                    <w:sectPr>
                      <w:headerReference w:type="default" r:id="rIdTocHeader"/>
                      <w:pgSz w:w="11906" w:h="16838"/>
                      <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>
                    </w:sectPr>
                  </w:pPr>
                </w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>目录</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>第一章 绪论</w:t></w:r></w:p>
                <w:p><w:r><w:t>模板正文占位</w:t></w:r></w:p>
                <w:p>
                  <w:pPr>
                    <w:sectPr>
                      <w:headerReference w:type="default" r:id="rIdBodyHeader"/>
                      <w:pgSz w:w="11906" w:h="16838"/>
                      <w:pgMar w:top="1134" w:right="1134" w:bottom="1134" w:left="1701" w:header="720" w:footer="720" w:gutter="0"/>
                    </w:sectPr>
                  </w:pPr>
                </w:p>
                <w:p><w:r><w:t>正文格式说明</w:t></w:r></w:p>
              </w:body>
            </w:document>
            """);
    }

    static void WriteFrontMatterDocx(string path, string title, string paragraphText, string tableValue)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddZipEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
            </Types>
            """);
        AddZipEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/styles.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style>
            </w:styles>
            """);
        AddZipEntry(
            archive,
            "word/document.xml",
            $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>{{SecurityElement.Escape(title)}}</w:t></w:r></w:p>
                <w:p><w:r><w:t>{{SecurityElement.Escape(paragraphText)}}</w:t></w:r></w:p>
                <w:tbl>
                  <w:tr><w:tc><w:p><w:r><w:t>字段</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>{{SecurityElement.Escape(tableValue)}}</w:t></w:r></w:p></w:tc></w:tr>
                </w:tbl>
                <w:sectPr><w:pgSz w:w="11906" w:h="16838"/></w:sectPr>
              </w:body>
            </w:document>
            """);
    }

    static void WriteCommentedRulesFixtureDocx(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddZipEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
              <Override PartName="/word/comments.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml"/>
            </Types>
            """);
        AddZipEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rIdComments" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="comments.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/styles.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
            </w:styles>
            """);
        AddZipEntry(
            archive,
            "word/comments.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:comments xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:comment w:id="0" w:author="teacher" w:date="2026-05-13T00:00:00Z">
                <w:p><w:r><w:t>正文首行缩进2字符，表格须采用三线表。</w:t></w:r></w:p>
              </w:comment>
            </w:comments>
            """);
        AddZipEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:r><w:t>格式要求：正文小四宋体，1.5倍行距。</w:t></w:r></w:p>
                <w:p>
                  <w:r><w:t>正文段落</w:t></w:r>
                  <w:commentRangeStart w:id="0"/>
                  <w:r><w:commentReference w:id="0"/></w:r>
                  <w:commentRangeEnd w:id="0"/>
                </w:p>
                <w:sectPr><w:pgSz w:w="11906" w:h="16838"/></w:sectPr>
              </w:body>
            </w:document>
            """);
    }

    static void WriteFormatMatchFixtureDocx(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddZipEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
            </Types>
            """);
        AddZipEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/styles.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="2"><w:name w:val="Plain Text"/></w:style>
            </w:styles>
            """);
        AddZipEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p>
                  <w:pPr>
                    <w:pStyle w:val="2"/>
                    <w:jc w:val="both"/>
                    <w:spacing w:line="360" w:lineRule="atLeast"/>
                    <w:ind w:firstLine="420" w:left="0"/>
                  </w:pPr>
                  <w:r>
                    <w:rPr><w:b w:val="false"/><w:i w:val="false"/><w:sz w:val="21"/></w:rPr>
                    <w:t>本文围绕系统设计与实现展开研究。</w:t>
                  </w:r>
                </w:p>
                <w:p>
                  <w:pPr>
                    <w:pStyle w:val="2"/>
                    <w:jc w:val="center"/>
                    <w:spacing w:line="360" w:lineRule="atLeast"/>
                    <w:ind w:firstLine="0" w:left="0"/>
                  </w:pPr>
                  <w:r>
                    <w:rPr><w:b/><w:i w:val="false"/><w:sz w:val="21"/></w:rPr>
                    <w:t>本文围绕标题样式展开说明。</w:t>
                  </w:r>
                </w:p>
                <w:p>
                  <w:pPr>
                    <w:pStyle w:val="2"/>
                    <w:jc w:val="both"/>
                    <w:spacing w:line="360" w:lineRule="atLeast"/>
                    <w:ind w:firstLine="420" w:left="0"/>
                  </w:pPr>
                  <w:r>
                    <w:rPr><w:b w:val="false"/><w:i w:val="false"/><w:sz w:val="24"/></w:rPr>
                    <w:t>本文围绕字号差异展开说明。</w:t>
                  </w:r>
                </w:p>
                <w:sectPr/>
              </w:body>
            </w:document>
            """);
    }

    static void WriteComplexScriptSizeFixtureDocx(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddZipEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
            </Types>
            """);
        AddZipEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/styles.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
            </w:styles>
            """);
        AddZipEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p>
                  <w:r>
                    <w:rPr><w:rFonts w:eastAsia="宋体"/><w:szCs w:val="21"/></w:rPr>
                    <w:t>正文字号只在 szCs 中声明</w:t>
                  </w:r>
                </w:p>
                <w:sectPr/>
              </w:body>
            </w:document>
            """);
    }

    static void AddZipEntry(ZipArchive archive, string entryName, string text)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    static void WriteSinglePixelPng(string path)
    {
        File.WriteAllBytes(
            path,
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
    }

    static void WriteDocxWithImageRelationshipIssues(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddZipEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Default Extension="png" ContentType="image/png"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);
        AddZipEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdImageRoot" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="/media/image1.png"/>
            </Relationships>
            """);
        var image = archive.CreateEntry("media/image1.png");
        using (var stream = image.Open())
        {
            var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
            stream.Write(bytes, 0, bytes.Length);
        }

        AddZipEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                        xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                        xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                        xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
              <w:body>
                <w:p>
                  <w:r>
                    <w:drawing>
                      <wp:inline>
                        <wp:extent cx="914400" cy="914400"/>
                        <wp:docPr id="1" name="bad-root-image"/>
                        <a:graphic>
                          <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                            <pic:pic>
                              <pic:nvPicPr><pic:cNvPr id="0" name="bad-root-image"/><pic:cNvPicPr/></pic:nvPicPr>
                              <pic:blipFill><a:blip r:embed="rIdImageRoot"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                              <pic:spPr/>
                            </pic:pic>
                          </a:graphicData>
                        </a:graphic>
                      </wp:inline>
                    </w:drawing>
                  </w:r>
                </w:p>
                <w:p>
                  <w:r>
                    <w:drawing>
                      <wp:inline>
                        <wp:extent cx="914400" cy="914400"/>
                        <wp:docPr id="2" name="missing-image"/>
                        <a:graphic>
                          <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                            <pic:pic>
                              <pic:nvPicPr><pic:cNvPr id="0" name="missing-image"/><pic:cNvPicPr/></pic:nvPicPr>
                              <pic:blipFill><a:blip r:embed="rIdMissing"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                              <pic:spPr/>
                            </pic:pic>
                          </a:graphicData>
                        </a:graphic>
                      </wp:inline>
                    </w:drawing>
                  </w:r>
                </w:p>
                <w:sectPr/>
              </w:body>
            </w:document>
            """);
    }

    static void WriteTemplateDocxWithExistingMediaImage(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddZipEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Default Extension="png" ContentType="image/png"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);
        AddZipEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdExistingImage" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
            </Relationships>
            """);
        var image = archive.CreateEntry("word/media/image1.png");
        using (var stream = image.Open())
        {
            var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
            stream.Write(bytes, 0, bytes.Length);
        }

        AddZipEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                        xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                        xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                        xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
              <w:body>
                <w:p><w:r><w:t>封面</w:t></w:r></w:p>
                <w:p>
                  <w:r>
                    <w:drawing>
                      <wp:inline>
                        <wp:extent cx="914400" cy="914400"/>
                        <wp:docPr id="1" name="existing-image"/>
                        <a:graphic>
                          <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                            <pic:pic>
                              <pic:nvPicPr><pic:cNvPr id="0" name="existing-image"/><pic:cNvPicPr/></pic:nvPicPr>
                              <pic:blipFill><a:blip r:embed="rIdExistingImage"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                              <pic:spPr/>
                            </pic:pic>
                          </a:graphicData>
                        </a:graphic>
                      </wp:inline>
                    </w:drawing>
                  </w:r>
                </w:p>
                <w:p><w:r><w:t>摘要</w:t></w:r></w:p>
                <w:p><w:r><w:t>模板正文占位</w:t></w:r></w:p>
                <w:p><w:r><w:t>格式说明</w:t></w:r><w:pPr><w:sectPr/></w:pPr></w:p>
              </w:body>
            </w:document>
            """);
    }

    static void WriteTemplateDocxWithSharedRootImageRelationships(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddZipEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Default Extension="png" ContentType="image/png"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
            </Types>
            """);
        AddZipEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
              <Relationship Id="rIdSharedA" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="/media/shared.png"/>
              <Relationship Id="rIdSharedB" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="/media/shared.png"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/_rels/header1.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdHeaderImage" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/header.png"/>
            </Relationships>
            """);
        AddImageEntry(archive, "media/shared.png");
        AddImageEntry(archive, "media/header.png");
        AddZipEntry(
            archive,
            "word/header1.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                   xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                   xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                   xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
              <w:p>
                <w:r>
                  <w:drawing>
                    <wp:inline>
                      <wp:extent cx="914400" cy="914400"/>
                      <wp:docPr id="3" name="header-image"/>
                      <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="header-image"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="rIdHeaderImage"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr/></pic:pic></a:graphicData></a:graphic>
                    </wp:inline>
                  </w:drawing>
                </w:r>
              </w:p>
            </w:hdr>
            """);
        AddZipEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                        xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                        xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                        xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
              <w:body>
                <w:p><w:r><w:t>封面</w:t></w:r></w:p>
                <w:p>
                  <w:r>
                    <w:drawing>
                      <wp:inline>
                        <wp:extent cx="914400" cy="914400"/>
                        <wp:docPr id="1" name="shared-a"/>
                        <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="shared-a"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="rIdSharedA"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr/></pic:pic></a:graphicData></a:graphic>
                      </wp:inline>
                    </w:drawing>
                  </w:r>
                </w:p>
                <w:p>
                  <w:r>
                    <w:drawing>
                      <wp:inline>
                        <wp:extent cx="914400" cy="914400"/>
                        <wp:docPr id="2" name="shared-b"/>
                        <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="shared-b"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="rIdSharedB"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr/></pic:pic></a:graphicData></a:graphic>
                      </wp:inline>
                    </w:drawing>
                  </w:r>
                </w:p>
                <w:p><w:r><w:t>摘要</w:t></w:r></w:p>
                <w:p><w:r><w:t>模板正文占位</w:t></w:r></w:p>
                <w:p><w:r><w:t>格式说明</w:t></w:r><w:pPr><w:sectPr><w:headerReference w:type="default" r:id="rIdHeader"/></w:sectPr></w:pPr></w:p>
              </w:body>
            </w:document>
            """);
    }

    static void AddImageEntry(ZipArchive archive, string entryName)
    {
        var image = archive.CreateEntry(entryName);
        using var stream = image.Open();
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        stream.Write(bytes, 0, bytes.Length);
    }

    static void InjectHyperlinkIntoFirstParagraph(string docxPath)
    {
        using var archive = ZipFile.Open(docxPath, ZipArchiveMode.Update);
        var entry = archive.GetEntry("word/document.xml") ?? throw new UnreachableException("Missing document.xml.");
        string xml;
        using (var reader = new StreamReader(entry.Open()))
        {
            xml = reader.ReadToEnd();
        }

        xml = xml.Replace(
            "<w:p><w:pPr><w:pStyle w:val=\"Title\"/></w:pPr><w:r><w:t>中文摘要</w:t></w:r></w:p>",
            "<w:p><w:pPr><w:pStyle w:val=\"Title\"/></w:pPr><w:hyperlink><w:r><w:t>中文摘要</w:t></w:r></w:hyperlink></w:p>",
            StringComparison.Ordinal);
        entry.Delete();
        AddZipEntry(archive, "word/document.xml", xml);
    }

    static void WriteSimpleDocx(string path, string bodyXml)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddZipEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);
        AddZipEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        AddZipEntry(
            archive,
            "word/document.xml",
            $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                {{bodyXml}}
                <w:sectPr/>
              </w:body>
            </w:document>
            """);
    }

}
