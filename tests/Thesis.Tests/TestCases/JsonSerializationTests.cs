internal static partial class Program
{
    static void JsonRoundtripUsesCamelCaseAndEnumStrings()
    {
        var request = new OperationRequest
        {
            SchemaVersion = "1.0",
            RequestId = "fix-abstract-001",
            Mode = RequestMode.ValidateOnly,
            Operations =
            [
                new ThesisOperation
                {
                    Id = "op-001",
                    Op = "replaceParagraph",
                    Target = JsonNode.Parse("""{"type":"role","role":"abstract.zh.body"}"""),
                    Text = "updated"
                }
            ]
        };

        var json = ThesisJson.Serialize(request);

        AssertContains(json, "\"schemaVersion\"");
        AssertContains(json, "\"requestId\"");
        AssertContains(json, "\"mode\":\"validateOnly\"");
        AssertDoesNotContain(json, "SchemaVersion");

        var roundtrip = ThesisJson.Deserialize<OperationRequest>(json);
        AssertEqual(RequestMode.ValidateOnly, roundtrip.Mode);
        AssertEqual("op-001", roundtrip.Operations[0].Id);
        AssertEqual(true, roundtrip.Options.CreateSnapshot);
        AssertEqual(true, roundtrip.Options.StopOnError);
        AssertEqual(false, roundtrip.Options.RequireSingleMatch);
        AssertEqual(false, roundtrip.Options.TrackChanges);
    }

    static void TemplateProfileRulesSerializeAsCamelCaseJson()
    {
        var profile = new TemplateProfile
        {
            RolePolicies =
            [
                new ProfileRolePolicy
                {
                    Role = "heading1",
                    AppliesTo = "paragraph",
                    Priority = 100,
                    Match = new ProfileRoleMatch
                    {
                        StyleIds = ["Heading1"],
                        TextPatterns = ["^第.+章"],
                        OutlineLevels = [0],
                        Format = new ProfileRoleFormatMatch
                        {
                            Alignment = "center",
                            FontSizeHalfPoints = "28",
                            Bold = true,
                            FirstLineIndentTwips = new IntRangeMatch { Min = 360, Max = 560 }
                        }
                    },
                    Format = new ParagraphFormatSample
                    {
                        Alignment = "center",
                        RunFormat = new RunFormatSample { Bold = true, FontSizeHalfPoints = "28" }
                    }
                }
            ],
            TableArchetypes =
            [
                new ProfileTableArchetype
                {
                    Name = "threeLine",
                    Confidence = 0.91,
                    Match = new ProfileTableMatch { MinRows = 2, ColumnCounts = [2, 3] },
                    Format = new TableFormatSample
                    {
                        Borders = new TableBordersSample
                        {
                            Top = new TableBorderLineSample { Value = "single", Size = "12" },
                            InsideVertical = new TableBorderLineSample { Value = "nil" }
                        }
                    }
                }
            ],
            Diagnostics =
            [
                new ProfileDiagnostic
                {
                    Severity = "info",
                    Code = "profile_rule_inferred",
                    Message = "heading1 policy inferred from style usage",
                    Evidence = ["style:Heading1", "paragraph:p1"]
                }
            ]
        };

        var json = ThesisJson.Serialize(profile);

        AssertContains(json, "\"rolePolicies\"");
        AssertContains(json, "\"appliesTo\":\"paragraph\"");
        AssertContains(json, "\"outlineLevels\":[0]");
        AssertContains(json, "\"format\"");
        AssertContains(json, "\"alignment\":\"center\"");
        AssertContains(json, "\"fontSizeHalfPoints\":\"28\"");
        AssertContains(json, "\"bold\":true");
        AssertContains(json, "\"firstLineIndentTwips\":{\"min\":360,\"max\":560");
        AssertContains(json, "\"tableArchetypes\"");
        AssertContains(json, "\"diagnostics\"");

        var roundtrip = ThesisJson.Deserialize<TemplateProfile>(json);
        AssertEqual(360, roundtrip.RolePolicies[0].Match.Format!.FirstLineIndentTwips!.Min);
    }

    static void FinalAuditModelsSerializeAsCamelCaseJson()
    {
        var result = new CliResult
        {
            FinalAudit = new FinalAuditReport
            {
                Ready = false,
                Readiness = "blocked",
                Summary = "Candidate has blocking findings.",
                Inputs = new Dictionary<string, string>
                {
                    ["template"] = "template.docx"
                },
                Outputs = new Dictionary<string, string>
                {
                    ["final"] = "final.docx"
                },
                Steps =
                [
                    new FinalAuditStep
                    {
                        Id = "assemble",
                        Status = "success",
                        Artifact = "assembled.docx"
                    }
                ],
                Blocking =
                [
                    new FinalAuditFinding
                    {
                        Id = "missing_reference_content",
                        Severity = "error",
                        Source = "rehearsal",
                        Message = "Reference content is missing.",
                        DiagnosticCode = "missing_reference_content"
                    }
                ]
            },
            RepairPlan = new RepairPlan
            {
                Ready = false,
                Items =
                [
                    new RepairPlanItem
                    {
                        IssueId = "missing_reference_content",
                        Severity = "error",
                        Source = "rehearsal",
                        TargetArtifact = "content.json",
                        SuggestedCommand = "Add the missing content to content.json and rerun finalize-all.",
                        Automatic = false,
                        RequiresWps = false,
                        Explanation = "The reference thesis contains content not present in the candidate."
                    }
                ]
            }
        };

        var json = ThesisJson.Serialize(result);

        AssertContains(json, "\"finalAudit\"");
        AssertContains(json, "\"repairPlan\"");
        AssertContains(json, "\"ready\":false");
        AssertContains(json, "\"blocking\"");
        AssertContains(json, "\"issueId\":\"missing_reference_content\"");
    }

}
