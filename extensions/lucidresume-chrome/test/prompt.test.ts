import test from "node:test";
import assert from "node:assert/strict";
import { mapFieldsWithPromptApi } from "../src/prompt";
import type { FormField, LedgerFact } from "../src/types";

test("uses Prompt API structured output and destroys the local session", async () => {
  let destroyed = false;
  let constraint: unknown;
  let prompt = "";
  let createOptions: LanguageModelCreateOptions | undefined;
  const original = Object.getOwnPropertyDescriptor(globalThis, "LanguageModel");
  Object.defineProperty(globalThis, "LanguageModel", {
    configurable: true,
    value: {
      availability: async () => "available",
      create: async (options: LanguageModelCreateOptions) => {
        createOptions = options;
        return {
          prompt: async (value: string, promptOptions: { responseConstraint?: unknown }) => {
            prompt = value;
            constraint = promptOptions.responseConstraint;
            return JSON.stringify({ mappings: [{
              field_id: "field_1", status: "proposal", fact_ids: ["entity:role"],
              extract: "Example Ltd", reason: "Exact employer substring"
            }] });
          },
          destroy: () => { destroyed = true; }
        };
      }
    }
  });

  try {
    const fields: FormField[] = [{
      id: "field_1", kind: "text", inputType: "text", label: "Employer", name: "employer",
      autocomplete: "", placeholder: "", required: true, options: []
    }];
    const facts: LedgerFact[] = [{
      id: "entity:role", kind: "entity", label: "Experience", value: "VP Engineering, Example Ltd", verified: true
    }];
    const result = await mapFieldsWithPromptApi(fields, facts);
    assert.equal(result[0]?.extract, "Example Ltd");
    assert.match(prompt, /closed-set evidence mapper/);
    assert.ok(constraint);
    assert.doesNotMatch(JSON.stringify(constraint), /uniqueItems/);
    assert.match(JSON.stringify(constraint), /"minItems":1/);
    assert.deepEqual(createOptions?.expectedInputs, [{ type: "text", languages: ["en"] }]);
    assert.deepEqual(createOptions?.expectedOutputs, [{ type: "text", languages: ["en"] }]);
    assert.equal(destroyed, true);
  } finally {
    if (original) Object.defineProperty(globalThis, "LanguageModel", original);
    else delete (globalThis as { LanguageModel?: unknown }).LanguageModel;
  }
});
