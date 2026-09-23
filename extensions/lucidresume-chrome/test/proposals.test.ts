import test from "node:test";
import assert from "node:assert/strict";
import { buildDeterministicProposals, projectModelMappings } from "../src/proposals";
import type { FormField, LedgerFact } from "../src/types";

const facts: LedgerFact[] = [
  { id: "identity:name", kind: "identity", label: "Resume owner", value: "Alex Example", verified: true },
  { id: "contact:email:1", kind: "contact", label: "Email", value: "alex@example.com", verified: true },
  { id: "entity:role", kind: "entity", label: "experience", value: "VP Engineering, Example Ltd", verified: true },
  { id: "prose:leadership", kind: "prose", label: "Human prose", value: "Led a 15 engineer TypeScript team through platform change on AWS.", verified: true }
];

const field = (partial: Partial<FormField>): FormField => ({
  id: "field_1", kind: "text", inputType: "text", label: "", name: "", autocomplete: "",
  placeholder: "", required: false, options: [], ...partial
});

test("directly maps standard contact fields without an LLM", () => {
  const result = buildDeterministicProposals([
    field({ id: "email", inputType: "email", label: "Email address" }),
    field({ id: "first", autocomplete: "given-name", label: "First name" }),
    field({ id: "last", autocomplete: "family-name", label: "Surname" })
  ], facts);
  assert.deepEqual(result.proposals.map(item => item.value), ["alex@example.com", "Alex", "Example"]);
  assert.ok(result.proposals.every(item => item.reason === "Direct match to a published ledger value."));
  assert.equal(result.unresolved.length, 0);
});

test("does not fill another person's contact fields from the candidate identity", () => {
  const result = buildDeterministicProposals([
    field({ id: "ref-name", label: "Referee full name" }),
    field({ id: "manager-email", inputType: "email", label: "Manager email" })
  ], facts);
  assert.equal(result.proposals.length, 0);
  assert.equal(result.unresolved.length, 2);
});

test("accepts only an exact substring of cited evidence for a short field", () => {
  const [proposal] = projectModelMappings(
    [field({ label: "Current employer" })], facts,
    [{ field_id: "field_1", status: "proposal", fact_ids: ["entity:role"], extract: "Example Ltd", reason: "Employer in role entity" }]
  );
  assert.equal(proposal?.value, "Example Ltd");
  assert.equal(proposal?.status, "review");
  assert.equal(proposal?.selected, false);
});

test("turns invented model output into an explicit gap", () => {
  const [proposal] = projectModelMappings(
    [field({ label: "Current employer" })], facts,
    [{ field_id: "field_1", status: "proposal", fact_ids: ["entity:role"], extract: "Invented Corp", reason: "Model guessed" }]
  );
  assert.equal(proposal?.status, "gap");
  assert.match(proposal?.reason ?? "", /not an exact substring/);
});

test("replaces an unhelpful model gap label with a deterministic explanation", () => {
  const [proposal] = projectModelMappings(
    [field({ label: "Current employer" })], facts,
    [{ field_id: "field_1", status: "gap", fact_ids: [], reason: "gap" }]
  );
  assert.equal(proposal?.status, "gap");
  assert.equal(proposal?.reason, "No supported ledger value was selected.");
});

test("projects long answers only from verified human prose", () => {
  const textarea = field({ kind: "textarea", inputType: "textarea", label: "Describe your leadership" });
  const [valid] = projectModelMappings([textarea], facts,
    [{ field_id: "field_1", status: "proposal", fact_ids: ["prose:leadership"], reason: "Relevant human prose" }]);
  const [invalid] = projectModelMappings([textarea], facts,
    [{ field_id: "field_1", status: "proposal", fact_ids: ["entity:role"], reason: "No human prose" }]);
  assert.equal(valid?.value, facts[3]?.value);
  assert.equal(valid?.status, "review");
  assert.deepEqual(valid?.factIds, ["prose:leadership"]);
  assert.equal(invalid?.status, "gap");
});

test("reports only the prose facts actually used for a long answer", () => {
  const textarea = field({ kind: "textarea", inputType: "textarea", label: "Describe your leadership" });
  const [proposal] = projectModelMappings([textarea], facts,
    [{ field_id: "field_1", status: "proposal", fact_ids: ["identity:name", "entity:role", "prose:leadership"], reason: "Relevant evidence" }]);
  assert.deepEqual(proposal?.factIds, ["prose:leadership"]);
  assert.deepEqual(proposal?.evidenceLabels, ["Human prose"]);
});

test("never silently selects model-derived proposals", () => {
  const select = field({ kind: "select", inputType: "select", label: "AWS experience", options: [
    { value: "yes", label: "Yes" }, { value: "no", label: "No" }
  ] });
  const [proposal] = projectModelMappings([select], facts,
    [{ field_id: "field_1", status: "proposal", fact_ids: ["prose:leadership"], option_value: "yes", reason: "AWS is explicit" }]);
  assert.equal(proposal?.status, "review");
  assert.equal(proposal?.selected, false);
});

test("rejects a work-authorisation answer backed by unrelated evidence", () => {
  const select = field({ kind: "select", inputType: "select", label: "Are you authorised to work in the UK?", options: [
    { value: "yes", label: "Yes" }, { value: "no", label: "No" }
  ] });
  const [proposal] = projectModelMappings([select], facts,
    [{ field_id: "field_1", status: "proposal", fact_ids: ["prose:leadership"], option_value: "yes", reason: "Model guessed" }]);
  assert.equal(proposal?.status, "gap");
  assert.match(proposal?.reason ?? "", /explicitly supports this work authorisation/);
});

test("allows a work-authorisation answer only with explicit ledger evidence", () => {
  const select = field({ kind: "select", inputType: "select", label: "Are you authorised to work in the UK?", options: [
    { value: "yes", label: "Yes" }, { value: "no", label: "No" }
  ] });
  const supportedFacts = [...facts, {
    id: "profile:work-authorisation", kind: "identity" as const, label: "Work authorisation",
    value: "Authorised to work in the UK without sponsorship", verified: true
  }];
  const [proposal] = projectModelMappings([select], supportedFacts,
    [{ field_id: "field_1", status: "proposal", fact_ids: ["profile:work-authorisation"], option_value: "yes", reason: "Explicit fact" }]);
  assert.equal(proposal?.status, "review");
  assert.equal(proposal?.value, "yes");
});
