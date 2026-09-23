import { buildFactCatalog } from "./facts";
import { isSupportedEndpoint, parseJobMlDocument, permissionOrigin, readJobMlResponse } from "./jobml";
import { mapFieldsWithPromptApi, promptAvailability } from "./prompt";
import { buildDeterministicProposals, projectModelMappings, selectPromptFacts } from "./proposals";
import type { FillProposal, FormField, LedgerFact } from "./types";

const endpointInput = element<HTMLInputElement>("endpoint");
const loadButton = element<HTMLButtonElement>("load");
const analyseButton = element<HTMLButtonElement>("analyse");
const fillButton = element<HTMLButtonElement>("fill");
const ledgerStatus = element<HTMLElement>("ledger-status");
const formStatus = element<HTMLElement>("form-status");
const modelStatus = element<HTMLElement>("model-status");
const results = element<HTMLElement>("results");
const proposalList = element<HTMLElement>("proposal-list");
const errorBox = element<HTMLElement>("error");

let facts: LedgerFact[] = [];
let proposals: FillProposal[] = [];
let activeTabId: number | undefined;

void initialise();
loadButton.addEventListener("click", () => void loadLedger());
analyseButton.addEventListener("click", () => void analyse());
fillButton.addEventListener("click", () => void fillReviewed());

async function initialise(): Promise<void> {
  const stored = await chrome.storage.local.get("jobMlEndpoint");
  if (typeof stored.jobMlEndpoint === "string") endpointInput.value = stored.jobMlEndpoint;
  try {
    const availability = await promptAvailability();
    modelStatus.textContent = availability === "unavailable"
      ? "Prompt API unavailable. Direct contact matching and honest gap reporting still work."
      : `Chrome Prompt API: ${availability}. Mapping runs locally in Chrome.`;
  } catch {
    modelStatus.textContent = "Could not inspect the Prompt API. Deterministic matching remains available.";
  }
}

async function loadLedger(): Promise<void> {
  clearError();
  loadButton.disabled = true;
  ledgerStatus.textContent = "Loading…";
  try {
    const url = isSupportedEndpoint(endpointInput.value.trim());
    const origin = permissionOrigin(url);
    const permitted = await chrome.permissions.contains({ origins: [origin] })
      || await chrome.permissions.request({ origins: [origin] });
    if (!permitted) throw new Error("Host access was not granted, so the ledger was not fetched.");

    const response = await fetch(url.href, {
      method: "GET",
      cache: "no-store",
      credentials: "omit",
      headers: { Accept: "text/markdown" }
    });
    if (!response.ok) throw new Error(`JobML endpoint returned HTTP ${response.status}.`);
    const document = parseJobMlDocument(await readJobMlResponse(response));
    document.revision = response.headers.get("etag")?.replaceAll('"', "") ?? undefined;
    facts = buildFactCatalog(document);
    if (facts.length === 0) throw new Error("The ledger is valid but contains no accepted, evidenced facts.");
    await chrome.storage.local.set({ jobMlEndpoint: url.href });
    analyseButton.disabled = false;
    ledgerStatus.textContent = `${facts.length} evidence-backed facts${document.revision ? ` · ${document.revision.slice(0, 10)}` : ""}`;
  } catch (error) {
    facts = [];
    analyseButton.disabled = true;
    ledgerStatus.textContent = "Not loaded";
    showError(error);
  } finally {
    loadButton.disabled = false;
  }
}

async function analyse(): Promise<void> {
  clearError();
  analyseButton.disabled = true;
  formStatus.textContent = "Reading visible empty fields…";
  try {
    const tab = await currentTab();
    if (!tab.id || !tab.url || !/^https?:/.test(tab.url))
      throw new Error("Open a normal HTTPS job application page before analysing.");
    activeTabId = tab.id;
    try {
      await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ["content.js"] });
    } catch {
      throw new Error("Chrome has not granted temporary access to this form. Open the form tab and click the extension toolbar icon again.");
    }
    const scan = await send<{ fields: FormField[]; title: string; url: string }>(tab.id, { type: "scan" });
    if (scan.fields.length === 0) throw new Error("No visible empty application fields were found in the main page.");

    const deterministic = buildDeterministicProposals(scan.fields, facts);
    let mapped: FillProposal[];
    const availability = await promptAvailability();
    if (deterministic.unresolved.length > 0 && availability !== "unavailable") {
      try {
        formStatus.textContent = `Mapping ${deterministic.unresolved.length} unresolved fields with Chrome's on-device model…`;
        mapped = [];
        const batches = chunk(deterministic.unresolved, 12);
        for (let index = 0; index < batches.length; index++) {
          const fields = batches[index]!;
          formStatus.textContent = `Mapping field batch ${index + 1} of ${batches.length} with Chrome's on-device model…`;
          const promptFacts = selectPromptFacts(fields, facts);
          const mappings = await mapFieldsWithPromptApi(fields, promptFacts, progress => {
            modelStatus.textContent = `Downloading Chrome's on-device model: ${Math.round(progress * 100)}%`;
          });
          mapped.push(...projectModelMappings(fields, promptFacts, mappings));
        }
        modelStatus.textContent = "Chrome Prompt API: available. Mapping ran locally in Chrome.";
      } catch {
        mapped = unresolvedGaps(deterministic.unresolved, "Chrome's on-device model could not run; no ledger answer was guessed.");
        modelStatus.textContent = "Chrome Prompt API could not run. Direct matches remain available and unresolved fields are gaps.";
      }
    } else {
      mapped = unresolvedGaps(deterministic.unresolved, "No direct ledger value matched and Chrome Prompt API is unavailable.");
    }
    proposals = [...deterministic.proposals, ...mapped];
    renderProposals(proposals);
    await send(tab.id, { type: "highlight", proposals: proposals.map(item => ({ fieldId: item.fieldId, status: item.status })) });
    const fillable = proposals.filter(item => item.status !== "gap").length;
    const gaps = proposals.length - fillable;
    formStatus.textContent = `${fillable} evidence-backed proposal${fillable === 1 ? "" : "s"}; ${gaps} gap${gaps === 1 ? "" : "s"}.`;
    results.classList.remove("hidden");
  } catch (error) {
    showError(error);
    formStatus.textContent = "Analysis stopped.";
  } finally {
    analyseButton.disabled = false;
  }
}

function renderProposals(items: FillProposal[]): void {
  proposalList.replaceChildren();
  for (const proposal of items) {
    const article = document.createElement("article");
    article.className = "proposal";
    const head = document.createElement("div");
    head.className = "proposal-head";
    if (proposal.status !== "gap") {
      const check = document.createElement("input");
      check.type = "checkbox";
      check.checked = proposal.selected;
      check.addEventListener("change", () => { proposal.selected = check.checked; updateFillButton(); });
      head.append(check);
    }
    const title = document.createElement("h3");
    title.textContent = proposal.label;
    head.append(title);
    article.append(head);
    const state = document.createElement("p");
    state.className = proposal.status === "gap" ? "gap" : proposal.status === "review" ? "review" : "";
    state.textContent = proposal.status === "gap" ? `Gap: ${proposal.reason}` : proposal.status === "review" ? `Review required: ${proposal.reason}` : proposal.reason;
    article.append(state);
    if (proposal.value) {
      const value = document.createElement("p");
      value.className = "value";
      value.textContent = proposal.value;
      article.append(value);
    }
    if (proposal.evidenceLabels.length) {
      const evidence = document.createElement("p");
      evidence.className = "evidence";
      evidence.textContent = `Evidence: ${proposal.evidenceLabels.join("; ")}`;
      article.append(evidence);
    }
    proposalList.append(article);
  }
  updateFillButton();
}

async function fillReviewed(): Promise<void> {
  clearError();
  if (!activeTabId) return;
  const chosen = proposals.filter(item => item.selected && item.value && item.status !== "gap");
  if (!chosen.length) return;
  fillButton.disabled = true;
  try {
    const tab = await currentTab();
    if (tab.id !== activeTabId)
      throw new Error("The active application tab changed. Analyse the current form again before filling.");
    const result = await send<{ applied: string[]; skipped: string[] }>(activeTabId, {
      type: "fill",
      values: chosen.map(item => ({ fieldId: item.fieldId, value: item.value }))
    });
    formStatus.textContent = `Filled ${result.applied.length}; skipped ${result.skipped.length} changed or unavailable fields. Nothing was submitted.`;
  } catch (error) { showError(error); }
  finally { fillButton.disabled = false; }
}

function updateFillButton(): void {
  fillButton.disabled = !proposals.some(item => item.selected && item.value && item.status !== "gap");
}

async function currentTab(): Promise<chrome.tabs.Tab> {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab) throw new Error("No active browser tab was found.");
  return tab;
}

async function send<T = Record<string, never>>(tabId: number, message: object): Promise<T> {
  const response = await chrome.tabs.sendMessage(tabId, message) as ({ ok: boolean; error?: string } & T) | undefined;
  if (!response?.ok) throw new Error(response?.error ?? "The application page did not respond.");
  return response;
}

function element<T extends HTMLElement>(id: string): T {
  const value = document.getElementById(id);
  if (!value) throw new Error(`Missing extension element '${id}'.`);
  return value as T;
}

function showError(error: unknown): void {
  errorBox.textContent = error instanceof Error ? error.message : String(error);
  errorBox.classList.remove("hidden");
}

function clearError(): void { errorBox.textContent = ""; errorBox.classList.add("hidden"); }

function chunk<T>(items: T[], size: number): T[][] {
  const result: T[][] = [];
  for (let index = 0; index < items.length; index += size) result.push(items.slice(index, index + size));
  return result;
}

function unresolvedGaps(fields: FormField[], reason: string): FillProposal[] {
  return fields.map(field => ({
    fieldId: field.id,
    label: field.label,
    status: "gap",
    factIds: [],
    evidenceLabels: [],
    selected: false,
    reason
  }));
}
