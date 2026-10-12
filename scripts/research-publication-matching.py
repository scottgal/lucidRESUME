#!/usr/bin/env python3
"""Offline article-discovery spike. Suggestions are not accepted career claims.

Indexes the blog's English Markdown files, including its actual date/category
syntax. No model, credentials, network access or application database is needed.
"""
import argparse
import collections
import datetime
import hashlib
import json
import math
import pathlib
import re
import time
import urllib.parse

STOP = set("a an and are as at be been by can for from has have in into is it its of on or our that the their these this to used using was were will with you your".split())


def tokens(text):
    return {word for word in re.findall(r"[a-z][a-z0-9+#.-]*", text.lower())
            if len(word) > 2 and word not in STOP}


def read_article(path, today):
    text = path.read_text(encoding="utf-8-sig")
    title = re.search(r"^#\s+(.+)$", text, re.M)
    date = re.search(r"<datetime\b[^>]*>(\d{4}-\d{2}-\d{2})", text, re.I)
    category = re.search(r"<!--\s*category\s*--\s*(.*?)-->", text, re.I | re.S)
    if not title or not date:
        return None
    try:
        published = datetime.date.fromisoformat(date[1])
    except ValueError:
        return None
    if published > today:
        return None
    categories = [value.strip() for value in category[1].split(",") if value.strip()] if category else []
    # Match MarkdownRenderingService.GetSlug: dot suffixes designate translations.
    if "." in path.stem:
        return None
    slug = path.stem.replace("_", "-").replace(" ", "-").lower()
    body = re.sub(r"```.*?```|<!--.*?-->|<[^>]+>", " ", text, flags=re.S)
    body = re.sub(r"!\[[^\]]*\]\([^)]*\)", " ", body)
    body = re.sub(r"\[([^\]]+)\]\([^)]*\)", r"\1", body)
    passages = [re.sub(r"\s+", " ", part).strip(" #>*-\n") for part in body.split("\n\n")]
    passages = [part for part in passages if len(part) >= 60]
    return dict(slug=slug, title=title[1].strip(), published=str(published), categories=categories,
                url="https://www.mostlylucid.net/blog/" + urllib.parse.quote(slug, safe=""),
                content_hash=hashlib.sha256(text.encode()).hexdigest(), passages=passages)


def rank(articles, query, limit):
    query_terms = tokens(query)
    frequency = collections.Counter(term for article in articles for term in tokens(
        article["title"] + " " + " ".join(article["categories"] + article["passages"])))
    weights = {term: math.log(1 + (len(articles) + 1) / (frequency[term] + 1)) for term in query_terms}
    results = []
    for article in articles:
        title = tokens(article["title"])
        categories = tokens(" ".join(article["categories"]))
        passages = [(sum(weights[term] for term in query_terms & tokens(passage)), passage)
                    for passage in article["passages"]]
        best = sorted(passages, key=lambda item: item[0], reverse=True)[:2]
        score = sum(weights[term] * 3 for term in query_terms & title)
        score += sum(weights[term] * 2 for term in query_terms & categories)
        score += sum(value for value, _ in best)
        if score <= 0:
            continue
        matches = sorted(query_terms & (title | categories | tokens(" ".join(p for _, p in best))))
        results.append({key: value for key, value in article.items() if key != "passages"} | dict(
            score=round(score, 4), matched_terms=matches,
            passages=[passage[:700] for value, passage in best if value > 0],
            evidence_type="authored-publication-candidate", review_status="suggested",
            live_url_verified=False))
    # Deterministic tie-breaks; one result per canonical slug.
    results.sort(key=lambda article: (-article["score"], article["slug"]))
    return results[:limit]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--markdown-root", required=True, type=pathlib.Path)
    query = parser.add_mutually_exclusive_group(required=True)
    query.add_argument("--query")
    query.add_argument("--query-file", type=pathlib.Path)
    parser.add_argument("--limit", type=int, default=5)
    parser.add_argument("--as-of", type=datetime.date.fromisoformat, default=datetime.date.today())
    parser.add_argument("--output", type=pathlib.Path)
    args = parser.parse_args()
    if not args.markdown_root.is_dir() or not 1 <= args.limit <= 50:
        parser.error("Provide an existing Markdown directory and a limit from 1 to 50.")
    started = time.perf_counter()
    articles = {}
    for path in sorted(args.markdown_root.glob("*.md")):
        article = read_article(path, args.as_of)
        if article:
            articles.setdefault(article["slug"], article)
    results = rank(list(articles.values()), args.query or args.query_file.read_text(), args.limit)
    report = dict(as_of=str(args.as_of), indexed_articles=len(articles),
                  elapsed_ms=round((time.perf_counter() - started) * 1000, 1),
                  method="weighted lexical title/category/best-two-passages; no stemming or embeddings",
                  limitations="Unlabelled discovery pilot; local files may be unpublished. Verify live URL, authorship and passage before acceptance.",
                  results=results)
    output = json.dumps(report, indent=2, ensure_ascii=False) + "\n"
    if args.output:
        args.output.write_text(output)
    else:
        print(output, end="")


if __name__ == "__main__":
    main()
