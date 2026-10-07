export type TextLanguage = 'markdown' | 'csharp' | 'text';

/** A multi-line string argument, which JSON would show as one line of `\n` escapes. */
export interface LongText {
  field: string;
  text: string;
  language: TextLanguage;
  /** What the text belongs to: the skill, `skill/script`, or the decision's key. */
  subject: string;
}

export interface ToolCallView {
  /** The skill an `execute_skill` or `execute_code` call names. */
  skill: string;
  /** Pretty-printed input with each long text cut to a preview. */
  json: string;
  texts: LongText[];
}

const PREVIEW = 80;

type Json = Record<string, unknown>;

export function toolCallView(inputJson: string): ToolCallView {
  let input: unknown;
  try {
    input = JSON.parse(inputJson);
  } catch {
    return { skill: '', json: inputJson, texts: [] };
  }
  if (!isObject(input)) {
    return { skill: '', json: JSON.stringify(input, null, 2), texts: [] };
  }

  const skill = typeof input['skill'] === 'string' ? input['skill'] : '';
  const args = isObject(input['args']) ? input['args'] : input;
  const subject = subjectOf(args) || skill;
  const texts: LongText[] = [];
  const shown: Json = { ...args };
  for (const [field, value] of Object.entries(args)) {
    if (typeof value === 'string' && value.includes('\n')) {
      texts.push({ field, text: value, language: languageOf(skill, field), subject });
      shown[field] = preview(value);
    }
  }
  const compact = args === input ? shown : { ...input, args: shown };
  return { skill, json: JSON.stringify(compact, null, 2), texts };
}

function languageOf(skill: string, field: string): TextLanguage {
  if (skill === 'create-code' && field === 'source') {
    return 'csharp';
  }
  return field === 'instructions' ? 'markdown' : 'text';
}

function subjectOf(args: Json): string {
  const text = (key: string) => (typeof args[key] === 'string' ? (args[key] as string) : '');
  if (text('skill') && text('script')) {
    return `${text('skill')}/${text('script')}`;
  }
  return text('name') || text('key') || text('id');
}

function preview(text: string): string {
  const head = text.split('\n', 1)[0].slice(0, PREVIEW);
  return `${head}… (${text.length.toLocaleString()} characters)`;
}

function isObject(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
