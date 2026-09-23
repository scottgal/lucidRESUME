# Alex Example

alex@example.com · +44 7700 900123 · https://github.com/alex

## Experience

### Example Ltd {#example-role}

<p id="example-leadership">
Led a 15 engineer TypeScript team through platform change on AWS.
</p>

---

```jobml
jobml:
  version: "0.1"
  purpose: Complete machine-readable evidence ledger for this resume.
  semantics:
    - Do not infer unsupported claims.
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
      capabilities: [engineering-leadership]
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
  - id: engineering-leadership
    type: capability
    name: Engineering Leadership
```
