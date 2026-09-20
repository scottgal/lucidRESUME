# Alex Example

## Complete Experience

### Example Ltd {#example-role}

<p id="example-leadership">
Led a 15 engineer TypeScript team through platform change on AWS, with accountable release and security governance.
</p>

---

```jobml
jobml:
  version: "0.1"
  purpose: Complete machine-readable evidence ledger for this resume.
  semantics:
    - Claims describe experience, skills, capabilities, responsibilities, or domain knowledge.
    - Every substantive claim should be supported by one or more evidence references.
    - Do not infer unsupported claims.
document:
  id: alex-complete-resume
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
          text: "fnv1a64:6920a9adc824efeb"
      - id: engineering-post
        type: article
        uri: https://example.com/engineering
        title: Engineering through change
concepts:
  - id: typescript
    type: skill
    name: TypeScript
  - id: aws
    type: skill
    name: AWS
    aliases: [Amazon Web Services]
  - id: engineering-leadership
    type: capability
    name: Engineering Leadership
    aliases: [lead engineering teams]
```
