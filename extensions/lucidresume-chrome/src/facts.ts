import { fnv1a64, normalizeText } from "./jobml";
import type { JobMlClaim, LedgerFact, ParsedJobMl } from "./types";

const emailPattern = /\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/gi;
const urlPattern = /https?:\/\/[^\s)>]+/gi;
const phonePattern = /(?<!\w)(?:\+?\d[\d .()\-]{7,}\d)(?!\w)/g;

export function buildFactCatalog(document: ParsedJobMl): LedgerFact[] {
  const facts: LedgerFact[] = [];
  const seen = new Set<string>();
  const add = (fact: LedgerFact) => {
    const key = `${fact.kind}\0${normalizeText(fact.value).toLocaleLowerCase()}`;
    if (fact.value.trim() && !seen.has(key)) { seen.add(key); facts.push(fact); }
  };

  const header = document.markdown.split(/^##\s+/m, 1)[0] ?? document.markdown;
  const firstHeading = header.match(/^#\s+(.+?)\s*$/m)?.[1];
  if (firstHeading) add({ id: "identity:name", kind: "identity", label: "Resume owner", value: normalizeText(firstHeading), verified: true });

  for (const [index, value] of [...header.matchAll(emailPattern)].map(match => match[0]).entries())
    add({ id: `contact:email:${index + 1}`, kind: "contact", label: "Email", value, verified: true });
  for (const [index, value] of [...header.matchAll(phonePattern)].map(match => match[0]).filter(value => (value.match(/\d/g) ?? []).length >= 9).entries())
    add({ id: `contact:phone:${index + 1}`, kind: "contact", label: "Phone", value: value.trim(), verified: true });
  for (const [index, value] of [...header.matchAll(urlPattern)].map(match => match[0]).entries())
    add({ id: `link:${index + 1}`, kind: "link", label: linkLabel(value), value, verified: true });

  for (const entity of document.data.entities ?? []) {
    if (!entity.id || !entity.name) continue;
    add({ id: `entity:${entity.id}`, kind: "entity", label: `${entity.type ?? "entity"}: ${entity.name}`, value: normalizeText(entity.name), verified: true });
  }

  const passages = buildPassageIndex(document.markdown);
  const conceptNames = new Map((document.data.concepts ?? []).filter(item => item.id)
    .map(item => [item.id!, item.name || item.id!]));
  for (const claim of document.data.claims ?? []) addClaimFacts(claim, conceptNames, passages, add);
  return facts;
}

function addClaimFacts(
  claim: JobMlClaim,
  concepts: Map<string, string>,
  passages: Map<string, string>,
  add: (fact: LedgerFact) => void
): void {
  if (!claim.id || !claim.statement || !isAcceptedClaim(claim)) return;
  const evidence = claim.supported_by ?? claim.evidence ?? [];
  const validProse = evidence.flatMap((item, index) => {
    if (isInvalidEvidenceState(item.state)) return [];
    const exact = resolveCurrentProse(item.ref, item.selector?.exact, passages);
    if (!exact) return [];
    const expected = item.fingerprint?.text;
    // A ref locates today's text but cannot prove it is the reviewed text.
    // A hash or exact selector is required before prose becomes a fact.
    if (!expected && !item.selector?.exact) return [];
    if (expected && expected.toLocaleLowerCase() !== fnv1a64(exact).toLocaleLowerCase()) return [];
    return [{ item, index, exact }];
  });
  const hasCurrentExternalEvidence = evidence.some(item => !!item.uri && !isInvalidEvidenceState(item.state));
  if (validProse.length === 0 && !hasCurrentExternalEvidence) return;

  add({
    id: `claim:${claim.id}`,
    kind: "claim",
    label: `Claim: ${claim.statement}`,
    value: normalizeText(claim.statement),
    claimId: claim.id,
    verified: true
  });

  for (const { item, index, exact } of validProse) {
    add({
      id: `prose:${item.id ?? `${claim.id}:${index + 1}`}`,
      kind: "prose",
      label: `Human prose supporting ${claim.id}`,
      value: normalizeText(exact),
      claimId: claim.id,
      evidenceRef: item.ref,
      verified: true
    });
  }

  for (const conceptId of [
    ...(claim.concepts?.skills ?? []),
    ...(claim.concepts?.capabilities ?? []),
    ...(claim.concepts?.domains ?? [])
  ]) {
    add({
      id: `concept:${claim.id}:${conceptId}`,
      kind: "concept",
      label: `Evidenced concept from ${claim.id}`,
      value: concepts.get(conceptId) ?? conceptId,
      claimId: claim.id,
      verified: true
    });
  }
}

function isInvalidEvidenceState(state: string | undefined): boolean {
  return ["changed", "missing", "ambiguous"].includes(state?.toLocaleLowerCase() ?? "");
}

function resolveCurrentProse(ref: string | undefined, selectedText: string | undefined, passages: Map<string, string>): string | undefined {
  const current = ref ? passages.get(normalizeRef(ref)) : undefined;
  const selector = selectedText ? normalizeText(selectedText) : undefined;
  if (ref && !current) return undefined;
  if (current && selector && current !== selector) return undefined;
  if (current) return current;
  if (!selector) return undefined;
  return [...passages.values()].filter(value => value === selector).length === 1 ? selector : undefined;
}

export function buildPassageIndex(markdown: string): Map<string, string> {
  const result = new Map<string, string>();
  const ambiguous = new Set<string>();
  const lines = markdown.replaceAll("\r\n", "\n").replaceAll("\r", "\n").split("\n");
  let headingId: string | undefined;
  let paragraphId: string | undefined;
  let paragraphNumber = 0;
  let inFence = false;
  let buffer: string[] = [];
  const add = (key: string, value: string) => {
    if (result.has(key)) { result.delete(key); ambiguous.add(key); }
    else if (!ambiguous.has(key)) result.set(key, value);
  };
  const flush = () => {
    const text = normalizeText(buffer.join(" "));
    buffer = [];
    if (!text) return;
    paragraphNumber++;
    const reference = paragraphId ? `#${paragraphId}` : headingId ? `#${headingId}:p${paragraphNumber}` : `#document:p${result.size + 1}`;
    add(reference.toLocaleLowerCase(), text);
    paragraphId = undefined;
  };
  for (const raw of lines) {
    const line = raw.trimEnd();
    if (line.trimStart().startsWith("```")) { flush(); inFence = !inFence; continue; }
    if (inFence) continue;
    const heading = line.match(/^(#{1,6})\s+(.*?)(?:\s+\{#([A-Za-z][A-Za-z0-9_.-]*)\})?\s*$/);
    if (heading) {
      flush();
      headingId = heading[3] || undefined;
      paragraphNumber = 0;
      continue;
    }
    const paragraph = line.match(/^\s*<p\s+id=["']([A-Za-z][A-Za-z0-9_.-]*)["']\s*>\s*$/i);
    if (paragraph) { flush(); paragraphId = paragraph[1]; continue; }
    if (line.trim().toLocaleLowerCase() === "</p>") { flush(); continue; }
    if (!line.trim() || /^(-{3,}|\*{3,}|_{3,})$/.test(line.trim())) { flush(); continue; }
    buffer.push(line.trim());
  }
  flush();
  return result;
}

function normalizeRef(value: string): string {
  return (value.startsWith("#") ? value : `#${value}`).toLocaleLowerCase();
}

function isAcceptedClaim(claim: JobMlClaim): boolean {
  const review = claim.review?.toLocaleLowerCase();
  return review === "accepted" || (!review && claim.origin?.toLocaleLowerCase() !== "derived");
}

function linkLabel(value: string): string {
  const url = new URL(value);
  const host = url.hostname.toLocaleLowerCase();
  const segments = url.pathname.split("/").filter(Boolean);
  if (host.includes("linkedin.com") && segments[0]?.toLocaleLowerCase() === "in" && segments.length === 2) return "LinkedIn";
  if (host.includes("github.com") && segments.length === 1) return "GitHub";
  return "Website";
}
