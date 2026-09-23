import { parse as parseYaml } from "yaml";
import type { JobMlRoot, ParsedJobMl } from "./types";

export const MAX_LEDGER_BYTES = 4 * 1024 * 1024;
const fencePattern = /^[ \t]*```jobml[ \t]*\n(?<yaml>.*?)[ \t]*```[ \t]*(?:\n|$)/gims;

export function parseJobMlDocument(source: string): ParsedJobMl {
  if (new TextEncoder().encode(source).byteLength > MAX_LEDGER_BYTES)
    throw new Error("The JobML document exceeds the 4 MB extension limit.");

  const normalized = source.replaceAll("\r\n", "\n").replaceAll("\r", "\n");
  const matches = [...normalized.matchAll(fencePattern)];
  if (matches.length !== 1)
    throw new Error(matches.length === 0
      ? "The endpoint did not return a Markdown document with one fenced jobml block."
      : "A full JobML document must contain exactly one fenced jobml block.");

  const yaml = matches[0]?.groups?.yaml ?? "";
  const value = parseYaml(yaml, { maxAliasCount: 50 }) as unknown;
  if (!value || typeof value !== "object" || Array.isArray(value))
    throw new Error("The JobML block is empty or is not an object.");
  const data = value as JobMlRoot;
  const version = typeof data.jobml === "string" ? data.jobml : data.jobml?.version;
  if (version !== "0.1") throw new Error(`Unsupported JobML version '${version ?? "missing"}'.`);
  if (!Array.isArray(data.claims) || !Array.isArray(data.entities) || !Array.isArray(data.concepts))
    throw new Error("JobML must contain claims, entities, and concepts arrays.");

  const match = matches[0]!;
  const markdown = (normalized.slice(0, match.index) + normalized.slice((match.index ?? 0) + match[0].length))
    .replace(/\n---[ \t]*\n+[ \t]*## MACHINE AREA[\s\S]*$/i, "")
    .replace(/\n---[ \t]*$/m, "")
    .trimEnd();
  return { markdown, data };
}

export async function readJobMlResponse(response: Response): Promise<string> {
  const declaredLength = Number(response.headers.get("content-length"));
  if (Number.isFinite(declaredLength) && declaredLength > MAX_LEDGER_BYTES)
    throw new Error("The JobML document exceeds the 4 MB extension limit.");

  if (!response.body) {
    const text = await response.text();
    if (new TextEncoder().encode(text).byteLength > MAX_LEDGER_BYTES)
      throw new Error("The JobML document exceeds the 4 MB extension limit.");
    return text;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let bytes = 0;
  let text = "";
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    bytes += value.byteLength;
    if (bytes > MAX_LEDGER_BYTES) {
      await reader.cancel();
      throw new Error("The JobML document exceeds the 4 MB extension limit.");
    }
    text += decoder.decode(value, { stream: true });
  }
  return text + decoder.decode();
}

export function normalizeText(text: string): string {
  return text
    .replace(/\[(.*?)]\([^)]*\)/g, "$1")
    .replace(/[*_`~]/g, "")
    .replace(/\s+/g, " ")
    .trim();
}

export function fnv1a64(text: string): string {
  let hash = 0xcbf29ce484222325n;
  const prime = 0x100000001b3n;
  for (const value of new TextEncoder().encode(normalizeText(text))) {
    hash ^= BigInt(value);
    hash = BigInt.asUintN(64, hash * prime);
  }
  return `fnv1a64:${hash.toString(16).padStart(16, "0")}`;
}

export function isSupportedEndpoint(value: string): URL {
  let url: URL;
  try { url = new URL(value); } catch { throw new Error("Enter a valid absolute JobML endpoint URL."); }
  const local = url.hostname === "localhost" || url.hostname === "127.0.0.1";
  if (url.protocol !== "https:" && !(local && url.protocol === "http:"))
    throw new Error("JobML endpoints must use HTTPS, except localhost development endpoints.");
  if (url.username || url.password) throw new Error("Credentials must not be embedded in the endpoint URL.");
  return url;
}

export function permissionOrigin(url: URL): string {
  return `${url.protocol}//${url.hostname}/*`;
}
