export type JobMlEvidence = {
  id?: string;
  type?: string;
  ref?: string;
  uri?: string;
  state?: string;
  fingerprint?: { text?: string };
  selector?: { type?: string; exact?: string; prefix?: string; suffix?: string };
};

export type JobMlClaim = {
  id?: string;
  subject?: string;
  statement?: string;
  review?: string;
  origin?: string;
  concepts?: { skills?: string[]; capabilities?: string[]; domains?: string[] };
  supported_by?: JobMlEvidence[];
  evidence?: JobMlEvidence[];
};

export type JobMlRoot = {
  jobml?: string | { version?: string; purpose?: string; semantics?: string[] };
  document?: { id?: string; language?: string; complete_ledger?: string };
  entities?: Array<{ id?: string; type?: string; name?: string; source?: string }>;
  claims?: JobMlClaim[];
  concepts?: Array<{ id?: string; type?: string; name?: string; aliases?: string[] }>;
};

export type ParsedJobMl = { markdown: string; data: JobMlRoot; revision?: string };

export type FactKind = "identity" | "contact" | "link" | "entity" | "claim" | "prose" | "concept";
export type LedgerFact = {
  id: string;
  kind: FactKind;
  value: string;
  label: string;
  claimId?: string;
  evidenceRef?: string;
  verified: boolean;
};

export type FieldKind = "text" | "textarea" | "select" | "checkbox" | "radio";
export type FormField = {
  id: string;
  kind: FieldKind;
  inputType: string;
  label: string;
  name: string;
  autocomplete: string;
  placeholder: string;
  required: boolean;
  maxLength?: number;
  options: Array<{ value: string; label: string }>;
};

export type ModelMapping = {
  field_id: string;
  status: "proposal" | "gap";
  fact_ids: string[];
  extract?: string;
  option_value?: string;
  reason: string;
};

export type FillProposal = {
  fieldId: string;
  label: string;
  status: "proposal" | "gap" | "review";
  value?: string;
  factIds: string[];
  evidenceLabels: string[];
  reason: string;
  selected: boolean;
};
