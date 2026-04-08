# Document Layout Detection

lucidRESUME uses a **YOLO DocLayNet model** to understand the visual structure of resume documents. This replaces the old template fingerprinting system that matched documents based on font styles and margins — which incorrectly treated structurally different documents as identical if they used the same fonts.

## How It Works

```
DOCX file
  → Morph renders to PNG image (150 DPI)
  → YOLOv10m DocLayNet (58MB ONNX model) detects regions
  → Returns bounding boxes: Title, Section-header, Text, Table, List-item, etc.
  → Structural hash computed from region positions
```

The model detects 11 region types:

| Region | What it identifies |
|--------|-------------------|
| **Title** | The candidate's name / document title |
| **Section-header** | "Experience", "Education", "Skills" headings |
| **Text** | Paragraph body text |
| **List-item** | Bulleted or numbered list entries |
| **Table** | Table structures (often used for two-column layouts) |
| **Picture** | Photos, logos, icons |
| **Page-header** | Running header at top of page |
| **Page-footer** | Running footer / page number |
| **Caption** | Image captions |
| **Footnote** | Footnotes |
| **Formula** | Mathematical formulas |

## Structural Hash

Each document gets a **structural perceptual hash** computed from its detected regions. The hash captures the layout pattern — where headings are, how content is arranged, whether there are columns or tables — without being affected by the actual text content.

Two documents with the same visual layout produce the same hash. Different layouts produce different hashes, even if they use identical fonts and styling.

### Example: Same Author, Different Templates

| Document | Regions | Hash | Layout |
|----------|---------|------|--------|
| Scott_Galloway_CTO.docx | 13 | `97ebed70f5792721` | Single column, sections |
| Scott_Galloway_Startup_Rescue.docx | 17 | `1cbe508fbc1db03c` | Narrative, different sections |

The old fingerprint system matched these as 100% identical (same fonts, same margins). The structural hash correctly identifies them as different layouts.

### Template Library Results

| Template | Regions | Hash | Style |
|----------|---------|------|-------|
| Chronological | 18 | `86ed945c876231c4` | Traditional sections |
| Harvard | 21 | `589345dad39ada20` | Detailed, many sections |
| Modern | 10 | `21beca9ccfe7a02d` | Clean, fewer sections |
| Two-Column | 4 | `4fb69cab990e4696` | Table-based 2-column |
| Technical | 13 | `2f8c96ea0b53bc7d` | Tech-focused sections |

Every template gets a unique hash. The Two-Column template has only 4 regions because the model detects the entire two-column table as one unit.

## Model

- **Architecture**: YOLOv10m
- **Training data**: IBM DocLayNet (80K+ annotated document pages)
- **Size**: 58MB (ONNX)
- **Inference**: ~100-200ms per page on CPU
- **Hosted**: [scottgal/doclaynet-yolov10m-onnx](https://huggingface.co/scottgal/doclaynet-yolov10m-onnx) on HuggingFace

### Lazy Loading

The model is **only downloaded and loaded when needed** — not at app startup. The first document parse that triggers layout detection will download the model (~58MB) to `%AppData%/lucidRESUME/models/doclaynet/`. Subsequent parses reuse the cached model.

## Future: Template Leiden Communities

The structural hashes feed into **Leiden community detection on templates** — the same algorithm used for skill communities. Each template is a node, structural similarity creates edges, and Leiden finds natural clusters:

- **"ATS Single-Column"** — traditional chronological formats
- **"Creative Two-Column"** — sidebar-based modern designs
- **"Table Layout"** — table-based two-column formats
- **"SDT Modern"** — Microsoft's content-control templates

The centroid of each cluster becomes the canonical template parser for that community. New documents are classified into the nearest community, and the community's parser handles section extraction.
