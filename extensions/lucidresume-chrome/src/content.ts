import type { FormField } from "./types";

type FillInstruction = { fieldId: string; value: string };
type ContentRequest =
  | { type: "scan" }
  | { type: "highlight"; proposals: Array<{ fieldId: string; status: string }> }
  | { type: "fill"; values: FillInstruction[] };

declare global {
  interface Window { __lucidResumeEvidenceFillerInstalled?: boolean }
}

if (!window.__lucidResumeEvidenceFillerInstalled) {
  window.__lucidResumeEvidenceFillerInstalled = true;
  const controls = new Map<string, HTMLElement>();
  const originalOutlines = new Map<HTMLElement, OriginalOutline>();

  chrome.runtime.onMessage.addListener((request: ContentRequest, _sender, respond) => {
    try {
      if (request.type === "scan") respond({ ok: true, fields: scan(controls, originalOutlines), title: document.title, url: location.href });
      else if (request.type === "highlight") { highlight(controls, originalOutlines, request.proposals); respond({ ok: true }); }
      else if (request.type === "fill") respond({ ok: true, ...fill(controls, request.values) });
      else respond({ ok: false, error: "Unknown request." });
    } catch (error) {
      respond({ ok: false, error: error instanceof Error ? error.message : String(error) });
    }
    return false;
  });
}

type OriginalOutline = { outline: string; outlinePriority: string; offset: string; offsetPriority: string };

function scan(controls: Map<string, HTMLElement>, originalOutlines: Map<HTMLElement, OriginalOutline>): FormField[] {
  restoreOutlines(originalOutlines);
  controls.clear();
  const elements = collectControls(document);
  const fields: FormField[] = [];
  const radioGroups = new WeakMap<object, Set<string>>();
  let index = 0;
  for (const element of elements) {
    if (!isEligible(element) || !isVisible(element) || hasUserValue(element)) continue;
    if (element instanceof HTMLInputElement && element.type === "radio" && element.name) {
      const scope = element.form ?? element.getRootNode();
      const names = radioGroups.get(scope) ?? new Set<string>();
      if (names.has(element.name)) continue;
      names.add(element.name);
      radioGroups.set(scope, names);
    }
    const id = `field_${++index}`;
    controls.set(id, element);
    fields.push(describe(element, id));
  }
  return fields.slice(0, 80);
}

function collectControls(root: Document | ShadowRoot): HTMLElement[] {
  const result = [...root.querySelectorAll<HTMLElement>("input, textarea, select, [contenteditable='true']")];
  for (const host of root.querySelectorAll<HTMLElement>("*"))
    if (host.shadowRoot) result.push(...collectControls(host.shadowRoot));
  return result;
}

function isEligible(element: HTMLElement): boolean {
  if (element.matches(":disabled, [readonly], [aria-disabled='true']")) return false;
  if (element instanceof HTMLInputElement)
    return !["hidden", "password", "file", "submit", "reset", "button", "image", "color", "range"].includes(element.type);
  return true;
}

function hasUserValue(element: HTMLElement): boolean {
  if (element instanceof HTMLInputElement) {
    if (["checkbox", "radio"].includes(element.type)) return element.checked;
    return element.value.trim().length > 0;
  }
  if (element instanceof HTMLSelectElement) {
    const selected = element.selectedOptions[0];
    if (!selected) return false;
    const placeholder = isPlaceholderOption(selected, element.selectedIndex);
    return !selected.disabled && !selected.hidden && !placeholder && element.value.trim().length > 0;
  }
  if (element instanceof HTMLTextAreaElement) return element.value.trim().length > 0;
  return (element.textContent ?? "").trim().length > 0;
}

function isVisible(element: HTMLElement): boolean {
  const style = getComputedStyle(element);
  const rect = element.getBoundingClientRect();
  return style.display !== "none" && style.visibility !== "hidden" && Number(style.opacity) !== 0 && rect.width > 1 && rect.height > 1;
}

function describe(element: HTMLElement, id: string): FormField {
  const input = element instanceof HTMLInputElement ? element : null;
  const select = element instanceof HTMLSelectElement ? element : null;
  const kind = element instanceof HTMLTextAreaElement || element.isContentEditable
    ? "textarea"
    : select ? "select" : input?.type === "checkbox" ? "checkbox" : input?.type === "radio" ? "radio" : "text";
  const maxLength = "maxLength" in element && typeof element.maxLength === "number" && element.maxLength > 0
    ? element.maxLength : undefined;
  return {
    id,
    kind,
    inputType: input?.type || (element.isContentEditable ? "contenteditable" : kind),
    label: fieldLabel(element),
    name: input?.name || select?.name || element.getAttribute("name") || "",
    autocomplete: input?.autocomplete || element.getAttribute("autocomplete") || "",
    placeholder: input?.placeholder || (element instanceof HTMLTextAreaElement ? element.placeholder : ""),
    required: "required" in element ? Boolean(element.required) : element.getAttribute("aria-required") === "true",
    maxLength,
    options: select
      ? [...select.options]
        .filter((option, optionIndex) => !option.disabled && option.value && !isPlaceholderOption(option, optionIndex))
        .map(option => ({ value: option.value, label: clean(option.textContent ?? option.label) }))
      : input?.type === "radio" ? radioOptions(input) : []
  };
}

function isPlaceholderOption(option: HTMLOptionElement, index: number): boolean {
  return index === 0 && /^(?:choose|select|please select|pick)(?:\s|\.|…|$)/i.test(clean(option.textContent ?? option.label));
}

function fieldLabel(element: HTMLElement): string {
  const input = element as HTMLInputElement;
  const labels = input.labels ? [...input.labels].map(labelText).join(" ") : "";
  const labelledBy = (element.getAttribute("aria-labelledby") ?? "").split(/\s+/).filter(Boolean)
    .map(id => document.getElementById(id)?.innerText ?? "").join(" ");
  const enclosingLabel = element.closest("label");
  const enclosing = enclosingLabel ? labelText(enclosingLabel) : "";
  const legend = element.closest("fieldset")?.querySelector("legend")?.textContent ?? "";
  const direct = clean(labels || labelledBy || element.getAttribute("aria-label") || enclosing || input.placeholder || input.name || "");
  if (input.type === "radio" && clean(legend)) return clean(legend).slice(0, 500);
  if (input.type === "checkbox" && clean(legend)) return clean(`${legend} ${direct}`).slice(0, 500);
  return clean(direct || legend || "Unlabelled field").slice(0, 500);
}

function labelText(label: HTMLLabelElement): string {
  const copy = label.cloneNode(true) as HTMLLabelElement;
  for (const control of copy.querySelectorAll("input, textarea, select, button")) control.remove();
  return copy.textContent ?? "";
}

function radioOptions(input: HTMLInputElement): Array<{ value: string; label: string }> {
  if (!input.name) return [{ value: input.value, label: fieldLabel(input) }];
  return queryRadioGroup(input)
    .map(item => ({ value: item.value, label: radioOptionLabel(item) }));
}

function radioOptionLabel(input: HTMLInputElement): string {
  const labels = input.labels ? [...input.labels].map(labelText).join(" ") : "";
  const aria = input.getAttribute("aria-label") ?? "";
  return clean(labels || aria || input.value);
}

function clean(value: string): string { return value.replace(/\s+/g, " ").trim(); }

function highlight(
  controls: Map<string, HTMLElement>,
  originalOutlines: Map<HTMLElement, OriginalOutline>,
  proposals: Array<{ fieldId: string; status: string }>
): void {
  restoreOutlines(originalOutlines);
  for (const proposal of proposals) {
    const element = controls.get(proposal.fieldId);
    if (!element) continue;
    originalOutlines.set(element, {
      outline: element.style.getPropertyValue("outline"),
      outlinePriority: element.style.getPropertyPriority("outline"),
      offset: element.style.getPropertyValue("outline-offset"),
      offsetPriority: element.style.getPropertyPriority("outline-offset")
    });
    const color = proposal.status === "gap" ? "#c33b55" : proposal.status === "review" ? "#b78100" : "#178273";
    element.style.setProperty("outline", `2px solid ${color}`, "important");
    element.style.setProperty("outline-offset", "2px", "important");
  }
}

function fill(controls: Map<string, HTMLElement>, values: FillInstruction[]): { applied: string[]; skipped: string[] } {
  const applied: string[] = [];
  const skipped: string[] = [];
  for (const instruction of values) {
    const element = controls.get(instruction.fieldId);
    if (!element || hasUserValue(element)) { skipped.push(instruction.fieldId); continue; }
    if (setValue(element, instruction.value)) applied.push(instruction.fieldId);
    else skipped.push(instruction.fieldId);
  }
  return { applied, skipped };
}

function setValue(element: HTMLElement, value: string): boolean {
  if (element instanceof HTMLSelectElement) {
    if (![...element.options].some(option => option.value === value)) return false;
    nativeSetter(HTMLSelectElement.prototype, element, value);
  } else if (element instanceof HTMLTextAreaElement) nativeSetter(HTMLTextAreaElement.prototype, element, value);
  else if (element instanceof HTMLInputElement) {
    if (element.type === "checkbox") nativeCheckedSetter(element, value === "true");
    else if (element.type === "radio") {
      const target = queryRadioGroup(element)
        .find(item => item.value === value);
      if (!target) return false;
      nativeCheckedSetter(target, true);
    } else nativeSetter(HTMLInputElement.prototype, element, value);
  } else if (element.isContentEditable) element.textContent = value;
  else return false;
  element.dispatchEvent(new InputEvent("input", { bubbles: true, inputType: "insertText", data: value }));
  element.dispatchEvent(new Event("change", { bubbles: true }));
  return true;
}

function nativeSetter(prototype: object, element: object, value: string): void {
  const setter = Object.getOwnPropertyDescriptor(prototype, "value")?.set;
  if (!setter) throw new Error("This field does not expose a value setter.");
  setter.call(element, value);
}

function nativeCheckedSetter(element: HTMLInputElement, value: boolean): void {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "checked")?.set;
  if (!setter) throw new Error("This field does not expose a checked setter.");
  setter.call(element, value);
}

function queryRadioGroup(input: HTMLInputElement): HTMLInputElement[] {
  const root = input.form ?? input.getRootNode();
  if (!(root instanceof Document || root instanceof ShadowRoot || root instanceof HTMLFormElement)) return [input];
  return [...root.querySelectorAll<HTMLInputElement>(`input[type=radio][name="${CSS.escape(input.name)}"]`)];
}

function restoreOutlines(originalOutlines: Map<HTMLElement, OriginalOutline>): void {
  for (const [element, original] of originalOutlines) {
    if (original.outline) element.style.setProperty("outline", original.outline, original.outlinePriority);
    else element.style.removeProperty("outline");
    if (original.offset) element.style.setProperty("outline-offset", original.offset, original.offsetPriority);
    else element.style.removeProperty("outline-offset");
  }
  originalOutlines.clear();
}
