const state = {
  profile: null,
  fileName: "",
};

function formatTwips(value) {
  if (value === null || value === undefined || value === "") {
    return "未设置";
  }

  const number = Number(value);
  if (!Number.isFinite(number)) {
    return String(value);
  }

  const cm = number / 567;
  return `${number} twips / ${cm.toFixed(2)} cm`;
}

function buildProfileSummary(profile) {
  const sourceDocument = profile?.sourceDocument ?? "";
  const sourceFile = sourceDocument
    ? sourceDocument.split(/[\\/]/).filter(Boolean).at(-1)
    : "未提供";
  const finalizationReasons = Array.isArray(profile?.finalizationReasons)
    ? profile.finalizationReasons
    : [];

  return {
    kind: profile?.profileKind ?? "未知",
    schemaVersion: profile?.schemaVersion ?? "未知",
    sourceFile,
    sourceType: profile?.sourceType ?? "未知",
    requiresFinalization: profile?.requiresFinalization === true,
    finalizationText: finalizationReasons.length > 0 ? finalizationReasons.join(", ") : "无",
    roleCount: Array.isArray(profile?.styleRoles) ? profile.styleRoles.length : 0,
    policyCount: Array.isArray(profile?.rolePolicies) ? profile.rolePolicies.length : 0,
    clusterCount: Array.isArray(profile?.formatClusters) ? profile.formatClusters.length : 0,
    tableCount: profile?.tablePolicy?.tableCount ?? 0,
    archetypeCount: Array.isArray(profile?.tableArchetypes) ? profile.tableArchetypes.length : 0,
    diagnosticCount: Array.isArray(profile?.diagnostics) ? profile.diagnostics.length : 0,
  };
}

function getRoleRows(profile) {
  const roles = Array.isArray(profile?.styleRoles) ? profile.styleRoles : [];
  return roles.map((role) => ({
    role: role.role ?? "未知",
    styleId: role.styleId ?? "未设置",
    name: role.name ?? "未命名",
    confidence: formatConfidence(role.confidence),
    alignment: role.format?.alignment ?? "继承",
    fontSize: role.format?.runFormat?.fontSizeHalfPoints
      ? `${Number(role.format.runFormat.fontSizeHalfPoints) / 2} pt`
      : "继承",
    eastAsiaFont: role.format?.runFormat?.eastAsiaFont
      ?? role.format?.runFormat?.asciiFont
      ?? "继承",
  }));
}

function getTableRows(profile) {
  const tables = Array.isArray(profile?.tableArchetypes) ? profile.tableArchetypes : [];
  return tables.map((table) => ({
    name: table.name ?? "未命名",
    confidence: formatConfidence(table.confidence),
    rows: rangeText(table.match?.minRows, table.match?.maxRows),
    columns: Array.isArray(table.match?.columnCounts)
      ? table.match.columnCounts.join(", ")
      : "未知",
    width: formatTwips(table.format?.widthTwips),
    alignment: table.format?.alignment ?? "继承",
    borders: borderText(table.format?.borders),
  }));
}

function searchJson(value, query) {
  const normalized = query.trim().toLowerCase();
  if (!normalized) {
    return [];
  }

  const results = [];
  walkJson(value, "", (path, current) => {
    if (String(current).toLowerCase().includes(normalized)) {
      results.push({ path, value: current });
    }
  });
  return results.slice(0, 200);
}

function walkJson(value, path, visit) {
  if (value === null || typeof value !== "object") {
    visit(path || "$", value);
    return;
  }

  if (Array.isArray(value)) {
    value.forEach((item, index) => {
      walkJson(item, `${path}[${index}]`, visit);
    });
    return;
  }

  Object.entries(value).forEach(([key, item]) => {
    walkJson(item, path ? `${path}.${key}` : key, visit);
  });
}

function formatConfidence(value) {
  const number = Number(value);
  return Number.isFinite(number) ? `${Math.round(number * 100)}%` : "未知";
}

function rangeText(min, max) {
  if (min === undefined && max === undefined) {
    return "未知";
  }

  if (min === max) {
    return String(min);
  }

  return `${min ?? "?"}-${max ?? "?"}`;
}

function borderText(borders) {
  if (!borders) {
    return "未设置";
  }

  const sides = [
    ["上", borders.top],
    ["下", borders.bottom],
    ["左", borders.left],
    ["右", borders.right],
    ["内横", borders.insideHorizontal],
    ["内竖", borders.insideVertical],
  ];
  return sides
    .map(([label, border]) => `${label}:${border?.value ?? "无"}`)
    .join(" ");
}

function $(selector) {
  return document.querySelector(selector);
}

function all(selector) {
  return Array.from(document.querySelectorAll(selector));
}

function setText(selector, text) {
  const element = $(selector);
  if (element) {
    element.textContent = text;
  }
}

function render(profile, fileName = "") {
  state.profile = profile;
  state.fileName = fileName;

  const summary = buildProfileSummary(profile);
  setText("#fileName", fileName || summary.sourceFile);
  setText("#profileKind", summary.kind);
  setText("#schemaVersion", summary.schemaVersion);
  setText("#sourceType", summary.sourceType);
  setText("#sourceFile", summary.sourceFile);
  setText("#finalization", summary.requiresFinalization ? `需要：${summary.finalizationText}` : "不需要");
  setText("#roleCount", summary.roleCount);
  setText("#policyCount", summary.policyCount);
  setText("#clusterCount", summary.clusterCount);
  setText("#tableCount", summary.tableCount);
  setText("#archetypeCount", summary.archetypeCount);
  setText("#diagnosticCount", summary.diagnosticCount);

  renderPageSetup(profile);
  renderRoles(profile);
  renderPolicies(profile);
  renderClusters(profile);
  renderTables(profile);
  renderDiagnostics(profile);
  renderRaw(profile);

  document.body.classList.add("has-profile");
}

function renderPageSetup(profile) {
  const setup = profile?.pageSetup ?? {};
  setText("#pageSize", `${formatTwips(setup.pageSize?.widthTwips)} × ${formatTwips(setup.pageSize?.heightTwips)}`);
  setText("#pageMargins", [
    `上 ${formatTwips(setup.margins?.topTwips)}`,
    `右 ${formatTwips(setup.margins?.rightTwips)}`,
    `下 ${formatTwips(setup.margins?.bottomTwips)}`,
    `左 ${formatTwips(setup.margins?.leftTwips)}`,
  ].join(" / "));
  setText("#headerFooter", `页眉 ${setup.headers?.length ?? 0} 个 / 页脚 ${setup.footers?.length ?? 0} 个`);
}

function renderRoles(profile) {
  const rows = getRoleRows(profile);
  $("#rolesBody").innerHTML = rows.map((row) => `
    <tr>
      <td>${escapeHtml(row.role)}</td>
      <td>${escapeHtml(row.styleId)}</td>
      <td>${escapeHtml(row.name)}</td>
      <td>${escapeHtml(row.confidence)}</td>
      <td>${escapeHtml(row.alignment)}</td>
      <td>${escapeHtml(row.fontSize)}</td>
      <td>${escapeHtml(row.eastAsiaFont)}</td>
    </tr>
  `).join("");
}

function renderPolicies(profile) {
  const policies = Array.isArray(profile?.rolePolicies) ? profile.rolePolicies : [];
  $("#policiesBody").innerHTML = policies.map((policy) => `
    <tr>
      <td>${escapeHtml(policy.role ?? "未知")}</td>
      <td>${escapeHtml(policy.priority ?? "未知")}</td>
      <td>${escapeHtml(formatConfidence(policy.confidence))}</td>
      <td>${escapeHtml((policy.match?.styleIds ?? []).join(", ") || "无")}</td>
      <td>${escapeHtml((policy.match?.outlineLevels ?? []).join(", ") || "无")}</td>
      <td>${escapeHtml((policy.match?.textPatterns ?? []).join(" | ") || "无")}</td>
    </tr>
  `).join("");
}

function renderClusters(profile) {
  const clusters = Array.isArray(profile?.formatClusters) ? profile.formatClusters : [];
  $("#clustersBody").innerHTML = clusters.map((cluster) => `
    <tr>
      <td>${escapeHtml(cluster.id ?? "未知")}</td>
      <td>${escapeHtml(cluster.roleHint ?? "未知")}</td>
      <td>${escapeHtml(cluster.count ?? "未知")}</td>
      <td>${escapeHtml(formatConfidence(cluster.confidence))}</td>
      <td>${escapeHtml((cluster.styleIds ?? []).join(", ") || "无")}</td>
      <td>${escapeHtml(cluster.format?.lineSpacing ?? "继承")}</td>
      <td>${escapeHtml(formatTwips(cluster.format?.firstLineIndentTwips))}</td>
    </tr>
  `).join("");
}

function renderTables(profile) {
  const rows = getTableRows(profile);
  $("#tablesBody").innerHTML = rows.map((row) => `
    <tr>
      <td>${escapeHtml(row.name)}</td>
      <td>${escapeHtml(row.confidence)}</td>
      <td>${escapeHtml(row.rows)}</td>
      <td>${escapeHtml(row.columns)}</td>
      <td>${escapeHtml(row.width)}</td>
      <td>${escapeHtml(row.alignment)}</td>
      <td>${escapeHtml(row.borders)}</td>
    </tr>
  `).join("");
}

function renderDiagnostics(profile) {
  const diagnostics = Array.isArray(profile?.diagnostics) ? profile.diagnostics : [];
  const container = $("#diagnosticsList");
  container.innerHTML = diagnostics.length
    ? diagnostics.map((diagnostic) => `
      <li>
        <strong>${escapeHtml(diagnostic.severity ?? "info")}</strong>
        <span>${escapeHtml(diagnostic.code ?? "diagnostic")}</span>
        <p>${escapeHtml(diagnostic.message ?? "")}</p>
      </li>
    `).join("")
    : "<li><strong>无诊断</strong><span>clean</span><p>profile 未报告风险项。</p></li>";
}

function renderRaw(profile) {
  $("#rawJson").textContent = JSON.stringify(profile, null, 2);
  renderSearchResults([]);
}

function renderSearchResults(results) {
  const container = $("#searchResults");
  container.innerHTML = results.length
    ? results.map((result) => `
      <button class="search-result" type="button" data-path="${escapeHtml(result.path)}">
        <span>${escapeHtml(result.path)}</span>
        <code>${escapeHtml(shortValue(result.value))}</code>
      </button>
    `).join("")
    : "<p class=\"muted\">输入关键词后显示匹配路径。</p>";
}

function shortValue(value) {
  const text = typeof value === "string" ? value : JSON.stringify(value);
  return text.length > 90 ? `${text.slice(0, 90)}...` : text;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;");
}

async function readFile(file) {
  const text = await file.text();
  try {
    const profile = JSON.parse(text);
    render(profile, file.name);
    setText("#loadStatus", "已载入");
  } catch (error) {
    setText("#loadStatus", `JSON 解析失败：${error.message}`);
  }
}

function setupUi() {
  const fileInput = $("#fileInput");
  const dropZone = $("#dropZone");

  fileInput?.addEventListener("change", (event) => {
    const file = event.target.files?.[0];
    if (file) {
      readFile(file);
    }
  });

  dropZone?.addEventListener("dragover", (event) => {
    event.preventDefault();
    dropZone.classList.add("is-dragging");
  });

  dropZone?.addEventListener("dragleave", () => {
    dropZone.classList.remove("is-dragging");
  });

  dropZone?.addEventListener("drop", (event) => {
    event.preventDefault();
    dropZone.classList.remove("is-dragging");
    const file = event.dataTransfer?.files?.[0];
    if (file) {
      readFile(file);
    }
  });

  $("#searchInput")?.addEventListener("input", (event) => {
    if (!state.profile) {
      return;
    }
    renderSearchResults(searchJson(state.profile, event.target.value));
  });

  all("[data-tab]").forEach((button) => {
    button.addEventListener("click", () => {
      all("[data-tab]").forEach((item) => item.classList.remove("active"));
      all("[data-panel]").forEach((panel) => panel.classList.remove("active"));
      button.classList.add("active");
      $(`[data-panel="${button.dataset.tab}"]`)?.classList.add("active");
    });
  });
}

globalThis.ProfileViewer = {
  buildProfileSummary,
  formatTwips,
  getRoleRows,
  getTableRows,
  searchJson,
};

if (typeof document !== "undefined") {
  setupUi();
}
