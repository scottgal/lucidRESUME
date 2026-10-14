"""Three-way NLI probe. Run once per corpus so model loading is measured separately."""
import argparse
import json
import time
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", choices=["support", "coverage"], default="support")
    parser.add_argument("--fixtures", type=Path)
    parser.add_argument("--model", default="MoritzLaurer/DeBERTa-v3-base-mnli-fever-anli")
    parser.add_argument("--revision", default="main")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    import torch
    from transformers import AutoModelForSequenceClassification, AutoTokenizer
    started = time.perf_counter()
    tokenizer = AutoTokenizer.from_pretrained(args.model, revision=args.revision)
    model = AutoModelForSequenceClassification.from_pretrained(args.model, revision=args.revision).eval()
    labels = {int(k): v.lower() for k, v in model.config.id2label.items()}
    if set(labels.values()) != {"entailment", "neutral", "contradiction"}:
        raise ValueError("This comparison requires a THREE-way NLI model, not a binary zero-shot classifier.")
    load_ms = (time.perf_counter() - started) * 1000
    rows = []
    fixtures = args.fixtures or Path(__file__).with_name("coverage-fixtures.json" if args.task == "coverage" else "fixtures.json")
    for case in json.loads(fixtures.read_text()):
        started = time.perf_counter()
        # Preserve the whole premise. Silent truncation could remove a negation or qualification.
        inputs = tokenizer(case["premise"], case["hypothesis"], return_tensors="pt", truncation=False)
        if inputs["input_ids"].shape[1] > model.config.max_position_embeddings:
            raise ValueError(f"{case['id']}: split evidence into scoped passages before inference")
        with torch.inference_mode():
            scores = model(**inputs).logits[0].softmax(dim=-1).tolist()
        probabilities = {labels[i]: score for i, score in enumerate(scores)}
        predicted = max(probabilities, key=probabilities.get)
        elapsed_ms = (time.perf_counter() - started) * 1000
        print(f"{case['id']:24} {predicted:14} {elapsed_ms:.0f}ms")
        rows.append(dict(id=case["id"], expected=case.get("expected"), predicted=predicted,
                         probabilities=probabilities, elapsedMs=elapsed_ms))
    args.output.write_text(json.dumps(dict(provider="nli", task=args.task, model=args.model,
        revision=getattr(model.config, "_commit_hash", args.revision), device="cpu", loadMs=load_ms,
        timing="Per-case tokenization and ONE forward pass, excluding model loading.", rows=rows), indent=2))


if __name__ == "__main__":
    main()
