import test from "node:test";
import assert from "node:assert/strict";
import { buildFactCatalog, buildPassageIndex } from "../src/facts";
import { fnv1a64, isSupportedEndpoint, MAX_LEDGER_BYTES, parseJobMlDocument, permissionOrigin, readJobMlResponse } from "../src/jobml";

const source = `# Alex Example

alex@example.com · +44 7700 900123 · https://github.com/alex

## Experience

### Example Ltd {#example-role}

<p id="example-leadership">
Led a 15 engineer TypeScript team through platform change on AWS.
</p>

---

\`\`\`jobml
jobml:
  version: "0.1"
document:
  id: alex
  language: en-GB
entities:
  - id: example-role
    type: experience
    name: VP Engineering, Example Ltd
    source: "#example-role"
claims:
  - id: leadership
    subject: example-role
    statement: Led an engineering team through platform change.
    review: accepted
    concepts:
      skills: [typescript, aws]
    supported_by:
      - id: leadership-prose
        type: prose
        ref: "#example-leadership"
        fingerprint:
          text: "fnv1a64:d3e4ad35fe5f6ba4"
concepts:
  - id: typescript
    type: skill
    name: TypeScript
  - id: aws
    type: skill
    name: AWS
\`\`\`
`;

test("parses a single full JobML document and removes the machine block", () => {
  const parsed = parseJobMlDocument(source);
  assert.equal(parsed.data.document?.id, "alex");
  assert.match(parsed.markdown, /Led a 15 engineer/);
  assert.doesNotMatch(parsed.markdown, /```jobml/);
});

test("builds contact, entity, claim, concept, and referenced prose facts", () => {
  const parsed = parseJobMlDocument(source);
  const facts = buildFactCatalog(parsed);
  assert.ok(facts.some(fact => fact.id === "identity:name" && fact.value === "Alex Example"));
  assert.ok(facts.some(fact => fact.id.startsWith("contact:email:") && fact.value === "alex@example.com"));
  assert.ok(facts.some(fact => fact.id === "entity:example-role"));
  assert.ok(facts.some(fact => fact.id === "claim:leadership"));
  assert.ok(facts.some(fact => fact.id === "prose:leadership-prose" && fact.value.startsWith("Led a 15")));
});

test("does not treat evidence-body numbers or repository links as contact details", () => {
  const expanded = source.replace(
    "Led a 15 engineer TypeScript team through platform change on AWS.",
    "Led a 15 engineer TypeScript team through platform change on AWS and 123456789 requests. https://github.com/alex/platform"
  ).replace("fnv1a64:d3e4ad35fe5f6ba4", "");
  const facts = buildFactCatalog(parseJobMlDocument(expanded));
  assert.equal(facts.filter(fact => fact.kind === "contact" && fact.label === "Phone").length, 1);
  assert.equal(facts.filter(fact => fact.label === "GitHub").length, 1);
});

test("matches the JobML FNV-1a drift fingerprint", () => {
  assert.equal(fnv1a64("Led the modernisation of a high-volume ASP.NET Core platform."), "fnv1a64:78cf6acd1fd870af");
});

test("does not expose prose whose stored fingerprint has drifted", () => {
  const changed = source.replace("fnv1a64:d3e4ad35fe5f6ba4", "fnv1a64:0000000000000000");
  const facts = buildFactCatalog(parseJobMlDocument(changed));
  assert.ok(!facts.some(fact => fact.kind === "prose"));
  assert.ok(!facts.some(fact => fact.id === "claim:leadership"));
  assert.ok(!facts.some(fact => fact.kind === "concept"));
});

test("does not promote ref-only prose that has no drift anchor", () => {
  const withoutFingerprint = source.replace(/\n        fingerprint:\n          text: "fnv1a64:[a-f0-9]+"/, "");
  const facts = buildFactCatalog(parseJobMlDocument(withoutFingerprint));
  assert.ok(!facts.some(fact => fact.id === "claim:leadership"));
  assert.ok(!facts.some(fact => fact.kind === "prose"));
});

test("accepts an exact selector as a drift anchor when no checksum is present", () => {
  const withSelector = source.replace(
    'ref: "#example-leadership"\n        fingerprint:\n          text: "fnv1a64:d3e4ad35fe5f6ba4"',
    'ref: "#example-leadership"\n        selector:\n          type: TextQuoteSelector\n          exact: Led a 15 engineer TypeScript team through platform change on AWS.'
  );
  const facts = buildFactCatalog(parseJobMlDocument(withSelector));
  assert.ok(facts.some(fact => fact.id === "claim:leadership"));
  assert.ok(facts.some(fact => fact.kind === "prose"));
});

test("does not trust a stale selector when the referenced human prose changed", () => {
  const withSelector = source.replace(
    'ref: "#example-leadership"',
    'ref: "#example-leadership"\n        selector:\n          type: TextQuoteSelector\n          exact: Led a 15 engineer TypeScript team through platform change on AWS.'
  ).replace(
    "Led a 15 engineer TypeScript team through platform change on AWS.\n</p>",
    "Contributed to a TypeScript platform change on AWS.\n</p>"
  );
  const facts = buildFactCatalog(parseJobMlDocument(withSelector));
  assert.ok(!facts.some(fact => fact.kind === "prose"));
});

test("indexes explicit and structural prose references", () => {
  const index = buildPassageIndex("## Work\n\n### Org {#org}\n\nFirst paragraph.\n\nSecond paragraph.");
  assert.equal(index.get("#org:p1"), "First paragraph.");
  assert.equal(index.get("#org:p2"), "Second paragraph.");
});

test("restricts endpoints to HTTPS and local HTTP and derives a narrow permission", () => {
  const url = isSupportedEndpoint("https://cv.example/jobml/current");
  assert.equal(permissionOrigin(url), "https://cv.example/*");
  assert.throws(() => isSupportedEndpoint("http://cv.example/jobml"), /HTTPS/);
  assert.doesNotThrow(() => isSupportedEndpoint("http://localhost:5080/jobml"));
  assert.throws(() => isSupportedEndpoint("https://name:secret@cv.example/jobml"), /Credentials/);
});

test("bounds the HTTP response before parsing it", async () => {
  const ordinary = await readJobMlResponse(new Response("small ledger"));
  assert.equal(ordinary, "small ledger");

  const oversized = new Response("not buffered", {
    headers: { "content-length": String(MAX_LEDGER_BYTES + 1) }
  });
  await assert.rejects(() => readJobMlResponse(oversized), /4 MB/);

  const streamed = new Response(new ReadableStream({
    start(controller) {
      controller.enqueue(new Uint8Array(MAX_LEDGER_BYTES + 1));
      controller.close();
    }
  }));
  await assert.rejects(() => readJobMlResponse(streamed), /4 MB/);
});
