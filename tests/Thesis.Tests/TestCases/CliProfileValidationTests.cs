internal static partial class Program
{
    static void CliRunReturnsProfileInvalidForMalformedWorkspaceProfile()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        File.WriteAllText(context.Paths.ProfileJson, "{not-json");
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-bad-profile",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "find-role",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "abstract.zh" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("profile_invalid", result.Diagnostics[0].Code);
    }

    static void CliRunReturnsProfileInvalidForStructurallyInvalidWorkspaceProfile()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        File.WriteAllText(
            context.Paths.ProfileJson,
            """
            {
              "schemaVersion": "1.0",
              "profileKind": "templateProfile",
              "styleRoles": null
            }
            """);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-bad-profile-shape",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "find-role",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "abstract.zh" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("profile_invalid", result.Diagnostics[0].Code);
    }

    static void CliRunReturnsProfileInvalidForNullRoleEvidence()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        File.WriteAllText(
            context.Paths.ProfileJson,
            """
            {
              "schemaVersion": "1.0",
              "profileKind": "templateProfile",
              "styleRoles": [
                {
                  "role": "abstract.zh",
                  "styleId": "Heading1",
                  "evidence": null
                }
              ]
            }
            """);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-null-evidence",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "find-role",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "abstract.zh" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("profile_invalid", result.Diagnostics[0].Code);
    }

    static void CliRunReturnsProfileInvalidForNullProfileRuleContainers()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        File.WriteAllText(
            context.Paths.ProfileJson,
            """
            {
              "schemaVersion": "1.0",
              "profileKind": "templateProfile",
              "rolePolicies": [
                {
                  "role": "heading1",
                  "appliesTo": "paragraph",
                  "match": null
                }
              ],
              "tableArchetypes": [
                {
                  "name": "threeLine",
                  "match": {
                    "columnCounts": null
                  }
                }
              ],
              "diagnostics": [
                {
                  "severity": "info",
                  "code": "profile_rule_inferred",
                  "message": "bad",
                  "evidence": null
                }
              ]
            }
            """);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-null-profile-rules",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "find-role",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "abstract.zh" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("profile_invalid", result.Diagnostics[0].Code);
    }

    static void CliRunReturnsProfileInvalidForMalformedRoleFormatRange()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var profile = new TemplateProfile
        {
            RolePolicies =
            [
                new ProfileRolePolicy
                {
                    Role = "body",
                    AppliesTo = "paragraph",
                    Match = new ProfileRoleMatch
                    {
                        TextPatterns = [".+"],
                        Format = new ProfileRoleFormatMatch
                        {
                            FirstLineIndentTwips = new IntRangeMatch { Exact = 480, Min = 360 }
                        }
                    }
                }
            ]
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-bad-format-range",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "find-body",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "body" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("profile_invalid", result.Diagnostics[0].Code);
    }

    static void CliRunReturnsProfileInvalidForMalformedFormatClusters()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        File.WriteAllText(
            context.Paths.ProfileJson,
            """
            {
              "schemaVersion": "1.0",
              "profileKind": "templateProfile",
              "sourceType": "test",
              "sourceDocument": "test.docx",
              "finalizationReasons": [],
              "pageSetup": {},
              "styleRoles": [],
              "rolePolicies": [],
              "formatClusters": [
                {
                  "id": "bad-cluster",
                  "appliesTo": "paragraph",
                  "roleHint": "body",
                  "styleIds": null,
                  "match": {
                    "styleIds": [],
                    "textPatterns": [],
                    "outlineLevels": [],
                    "format": {
                      "firstLineIndentTwips": {}
                    }
                  },
                  "evidence": []
                }
              ],
              "numberingPolicy": {
                "instances": [],
                "paragraphUses": []
              },
              "tablePolicy": {
                "observedColumnCounts": []
              },
              "tableArchetypes": [],
              "diagnostics": [],
              "sourceEvidence": {
                "paragraphSamples": []
              }
            }
            """);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "requestId": "req-bad-format-cluster",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "find-body",
                  "op": "resolveTarget",
                  "target": { "type": "role", "role": "body" }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("profile_invalid", result.Diagnostics[0].Code);
    }

}
