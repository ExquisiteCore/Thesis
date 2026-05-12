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

console.log("profile-viewer tests passed");
