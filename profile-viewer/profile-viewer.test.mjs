import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";

const script = fs.readFileSync(new URL("./app.js", import.meta.url), "utf8");
const sandbox = { console };
sandbox.globalThis = sandbox;
vm.createContext(sandbox);
vm.runInContext(script, sandbox);

const {
  buildProfileSummary,
  formatTwips,
  getRoleRows,
  getTableRows,
  searchJson,
} = sandbox.ProfileViewer;

const profile = {
  schemaVersion: "1.0",
  profileKind: "templateProfile",
  sourceDocument: "D:\\project\\lunwen-word\\lizi\\template.docx",
  requiresFinalization: true,
  finalizationReasons: ["fields", "toc"],
  pageSetup: {
    pageSize: { widthTwips: 11906, heightTwips: 16838 },
    margins: {
      topTwips: 1440,
      rightTwips: 1247,
      bottomTwips: 1440,
      leftTwips: 1701,
    },
  },
  styleRoles: [
    { role: "references", styleId: "2", confidence: 0.82 },
    { role: "acknowledgements", styleId: "2", confidence: 0.81 },
  ],
  tablePolicy: {
    detected: true,
    tableCount: 2,
    observedColumnCounts: [2, 13],
  },
  tableArchetypes: [
    { name: "tableFormat1", confidence: 0.66, match: { columnCounts: [2] } },
  ],
  formatClusters: [{ id: "paragraph-format-1" }, { id: "paragraph-format-2" }],
  diagnostics: [{ severity: "warning", code: "weak_profile" }],
};

const summary = buildProfileSummary(profile);
assert.equal(summary.kind, "templateProfile");
assert.equal(summary.sourceFile, "template.docx");
assert.equal(summary.roleCount, 2);
assert.equal(summary.tableCount, 2);
assert.equal(summary.requiresFinalization, true);
assert.equal(summary.finalizationText, "fields, toc");

assert.equal(formatTwips(1440), "1440 twips / 2.54 cm");
assert.equal(formatTwips(null), "未设置");

const roles = getRoleRows(profile);
assert.deepEqual(
  roles.map((role) => role.role),
  ["references", "acknowledgements"],
);

const tables = getTableRows(profile);
assert.equal(tables.length, 1);
assert.equal(tables[0].columns, "2");

const results = searchJson(profile, "references");
assert.equal(results.some((result) => result.path === "styleRoles[0].role"), true);

const finalSummary = buildProfileSummary(profile, "final-rules.json");
assert.equal(finalSummary.kind, "finalRules");

const projectRules = {
  schemaVersion: "1.0",
  rulesKind: "projectRules",
  roleAliases: { mainBody: "body" },
  pageSetup: {
    margins: { leftTwips: 1701 },
  },
  roleFormats: {
    body: {
      firstLineIndentTwips: 480,
      lineSpacing: "360",
      fontSizeHalfPoints: "24",
      eastAsiaFont: "宋体",
    },
  },
  tableDefault: {
    widthTwips: 8307,
    borders: {
      top: { value: "single" },
      left: { value: "nil" },
    },
  },
  tableArchetypes: [
    { name: "threeLine", confidence: 0.9, match: { columnCounts: [3] } },
  ],
  diagnostics: [{ severity: "info", code: "ai_rule" }],
};

const projectSummary = buildProfileSummary(projectRules, "project-rules.json");
assert.equal(projectSummary.kind, "projectRules");
assert.equal(projectSummary.sourceFile, "项目规则 JSON");
assert.equal(projectSummary.roleCount, 1);
assert.equal(projectSummary.aliasCount, 1);
assert.equal(projectSummary.tableCount, 1);
assert.equal(projectSummary.archetypeCount, 1);

const projectRoleRows = getRoleRows(projectRules);
assert.equal(projectRoleRows[0].role, "body");
assert.equal(projectRoleRows[0].fontSize, "12 pt");
assert.equal(projectRoleRows[0].eastAsiaFont, "宋体");

const projectTableRows = getTableRows(projectRules);
assert.equal(projectTableRows.length, 2);
assert.equal(projectTableRows[0].name, "tableDefault");
assert.equal(projectTableRows[0].width, "8307 twips / 14.65 cm");
assert.equal(projectTableRows[0].borders.includes("上:single"), true);

console.log("profile-viewer tests passed");
