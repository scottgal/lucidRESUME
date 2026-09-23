import { normalizeText } from "./jobml";
import type { FillProposal, FormField, LedgerFact, ModelMapping } from "./types";

export function buildDeterministicProposals(fields: FormField[], facts: LedgerFact[]): {
  proposals: FillProposal[];
  unresolved: FormField[];
} {
  const proposals: FillProposal[] = [];
  const unresolved: FormField[] = [];
  for (const field of fields) {
    const match = deterministicMatch(field, facts);
    if (match) proposals.push(match);
    else unresolved.push(field);
  }
  return { proposals, unresolved };
}

function deterministicMatch(field: FormField, facts: LedgerFact[]): FillProposal | null {
  if (field.kind !== "text") return null;
  const hint = normalizeText(`${field.autocomplete} ${field.name} ${field.label} ${field.placeholder}`).toLocaleLowerCase();
  const describesAnotherPerson = /\b(reference|referee|manager|supervisor|emergency|recruiter|contact person)\b/.test(hint);
  const find = (predicate: (fact: LedgerFact) => boolean) => facts.find(fact => fact.verified && predicate(fact));
  let fact: LedgerFact | undefined;
  let value: string | undefined;

  if (!describesAnotherPerson && (field.inputType === "email" || /\b(e-?mail)\b/.test(hint))) fact = find(item => item.id.startsWith("contact:email:"));
  else if (!describesAnotherPerson && (field.inputType === "tel" || /\b(phone|telephone|mobile)\b/.test(hint))) fact = find(item => item.id.startsWith("contact:phone:"));
  else if (/linkedin/.test(hint)) fact = find(item => item.label === "LinkedIn");
  else if (/github/.test(hint)) fact = find(item => item.label === "GitHub");
  else if (field.inputType === "url" || /\b(website|portfolio|personal url)\b/.test(hint))
    fact = find(item => item.kind === "link" && item.label !== "LinkedIn" && item.label !== "GitHub");
  else if (!describesAnotherPerson && /\b(given-name|first name|forename)\b/.test(hint)) {
    fact = find(item => item.id === "identity:name");
    value = fact?.value.split(/\s+/)[0];
  } else if (!describesAnotherPerson && /\b(family-name|last name|surname)\b/.test(hint)) {
    fact = find(item => item.id === "identity:name");
    value = fact?.value.split(/\s+/).at(-1);
  } else if (!describesAnotherPerson && (/\bname\b/.test(field.autocomplete) || /^(?:your |candidate )?(?:full )?name\s*$/.test(normalizeText(field.label).toLocaleLowerCase())))
    fact = find(item => item.id === "identity:name");

  value ??= fact?.value;
  if (!fact || !value || (field.maxLength && value.length > field.maxLength)) return null;
  return {
    fieldId: field.id,
    label: field.label,
    status: "proposal",
    value,
    factIds: [fact.id],
    evidenceLabels: [fact.label],
    reason: "Direct match to a published ledger value.",
    selected: true
  };
}

export function projectModelMappings(
  fields: FormField[],
  facts: LedgerFact[],
  mappings: ModelMapping[]
): FillProposal[] {
  const fieldMap = new Map(fields.map(field => [field.id, field]));
  const factMap = new Map(facts.map(fact => [fact.id, fact]));
  const mappingMap = new Map<string, ModelMapping>();
  for (const mapping of mappings) if (fieldMap.has(mapping.field_id) && !mappingMap.has(mapping.field_id))
    mappingMap.set(mapping.field_id, mapping);

  return fields.map(field => {
    const mapping = mappingMap.get(field.id);
    if (!mapping || mapping.status === "gap")
      return gap(field, usefulGapReason(mapping?.reason) ?? "No supported ledger value was selected.");
    const selectedFacts = [...new Set(mapping.fact_ids)].map(id => factMap.get(id)).filter((fact): fact is LedgerFact => !!fact?.verified);
    if (selectedFacts.length === 0) return gap(field, "The model did not select valid evidence.");
    const sensitiveReason = unsupportedSensitiveField(field, selectedFacts);
    if (sensitiveReason) return gap(field, sensitiveReason);

    const projected = projectValue(field, selectedFacts, mapping);
    if (!projected.value) return gap(field, projected.reason);
    if (field.maxLength && projected.value.length > field.maxLength)
      return gap(field, `Supported prose is ${projected.value.length} characters but this field allows ${field.maxLength}; edit it manually.`);
    return {
      fieldId: field.id,
      label: field.label,
      status: projected.requiresReview ? "review" : "proposal",
      value: projected.value,
      factIds: projected.facts.map(fact => fact.id),
      evidenceLabels: projected.facts.map(fact => fact.label),
      reason: mapping.reason || "Selected from verified JobML evidence.",
      selected: false
    };
  });
}

function usefulGapReason(reason: string | undefined): string | undefined {
  const value = reason?.trim();
  return value && !/^(?:gap|none|n\/?a|unknown)$/i.test(value) ? value : undefined;
}

const sensitiveCategories = [
  { field: /\b(authori[sz](?:ed|ation)|right to work|work permit|visa|sponsor(?:ship)?)\b/i,
    evidence: /\b(authori[sz](?:ed|ation)|right to work|work permit|visa|sponsor(?:ship)?)\b/i,
    name: "work authorisation or sponsorship" },
  { field: /\b(salary|compensation|remuneration|pay expectation|desired pay)\b/i,
    evidence: /\b(salary|compensation|remuneration|pay expectation|desired pay)\b/i,
    name: "salary expectation" },
  { field: /\b(availability|available to start|start date|notice period)\b/i,
    evidence: /\b(availability|available to start|start date|notice period)\b/i,
    name: "availability" },
  { field: /\b(gender|sex|ethnic(?:ity)?|race|racial|disab(?:ility|led)|veteran|religion|sexual orientation|pronouns?)\b/i,
    evidence: /\b(gender|sex|ethnic(?:ity)?|race|racial|disab(?:ility|led)|veteran|religion|sexual orientation|pronouns?)\b/i,
    name: "demographic information" },
  { field: /\b(motivation|why (?:do )?you want|why (?:this|our)|cover letter)\b/i,
    evidence: /\b(motivation|why (?:do )?you want|why (?:this|our)|cover letter)\b/i,
    name: "motivation" }
];

function unsupportedSensitiveField(field: FormField, facts: LedgerFact[]): string | undefined {
  const hint = `${field.label} ${field.name} ${field.placeholder}`;
  if (/\b(consent|agree|privacy|terms|declaration|certif(?:y|ication))\b/i.test(hint))
    return "Consent and declarations require an explicit user decision.";
  const category = sensitiveCategories.find(item => item.field.test(hint));
  if (!category) return undefined;
  const directlySupported = facts.some(fact => category.evidence.test(`${fact.label} ${fact.value}`));
  return directlySupported ? undefined : `No cited ledger fact explicitly supports this ${category.name} answer.`;
}

function projectValue(field: FormField, facts: LedgerFact[], mapping: ModelMapping): {
  value?: string;
  reason: string;
  requiresReview: boolean;
  facts: LedgerFact[];
} {
  if (field.kind === "textarea") {
    const prose = facts.filter(fact => fact.kind === "prose");
    return prose.length
      ? { value: prose.map(fact => fact.value).join("\n\n"), reason: "", requiresReview: true, facts: prose }
      : { reason: "This long-answer field needs human prose, but no selected evidence contains verified source prose.", requiresReview: true, facts: [] };
  }
  if (field.kind === "select" || field.kind === "radio") {
    const option = field.options.find(item => item.value === mapping.option_value || item.label === mapping.option_value);
    return option
      ? { value: option.value, reason: "", requiresReview: true, facts }
      : { reason: "The suggested option was not one of the page's current choices.", requiresReview: true, facts: [] };
  }
  if (field.kind === "checkbox")
    return { value: "true", reason: "", requiresReview: true, facts };

  const extract = normalizeText(mapping.extract ?? "");
  if (!extract) return { reason: "No exact ledger substring was selected.", requiresReview: true, facts: [] };
  for (const fact of facts) {
    const index = fact.value.toLocaleLowerCase().indexOf(extract.toLocaleLowerCase());
    if (index >= 0)
      return { value: fact.value.slice(index, index + extract.length), reason: "", requiresReview: true, facts: [fact] };
  }
  return { reason: "The proposed value was not an exact substring of its cited ledger evidence.", requiresReview: true, facts: [] };
}

function gap(field: FormField, reason: string): FillProposal {
  return { fieldId: field.id, label: field.label, status: "gap", factIds: [], evidenceLabels: [], reason, selected: false };
}

export function selectPromptFacts(fields: FormField[], facts: LedgerFact[], limit = 40): LedgerFact[] {
  const query = tokenize(fields.map(field => `${field.label} ${field.name} ${field.placeholder}`).join(" "));
  return facts
    .map((fact, index) => ({
      fact,
      index,
      score: ["identity", "contact", "link"].includes(fact.kind) ? 100 : overlap(query, tokenize(`${fact.label} ${fact.value}`))
    }))
    .sort((left, right) => right.score - left.score || left.index - right.index)
    .slice(0, limit)
    .map(item => item.fact);
}

function tokenize(value: string): Set<string> {
  return new Set(value.toLocaleLowerCase().match(/[a-z0-9+#.]{3,}/g) ?? []);
}

function overlap(left: Set<string>, right: Set<string>): number {
  let score = 0;
  for (const value of left) if (right.has(value)) score++;
  return score;
}
