---
name: Education matching requirements
description: Education equivalence matching across countries and JD requirements — needed for both candidate profiles and JD matching
type: project
---

Education matching needs two layers:

1. **JD requirements**: JDs specify "PhD", "Masters in CS", "BSc minimum" — the matcher needs to understand education levels and compare against candidate education
2. **Cross-country equivalence**: An Indian Administrative Exam, a UK BSc, a German Diplom — need equivalence mapping
3. **Anonymisation (SaaS only)**: Public profiles show ranked tiers ("Top 50 UK university") not specific names — locally always full data
4. **Why:** De-biases hiring — "5th Rank University in India" tells the employer quality without revealing identity. Specific institution only revealed if candidate chooses.

**How to apply:** When building the commercial SaaS anonymisation layer, and when enhancing the matching engine for education requirements in JDs.
