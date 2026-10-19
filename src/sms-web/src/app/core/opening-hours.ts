/** Small parser/formatter for the common subset of the OSM opening_hours syntax; anything else is left to the caller as raw text. */
interface Span { from: string; to: string; }   // "12:00" | "sunrise" ...
interface Rule { months: [number, number][] | null; days: number[] | null; ph: boolean; spans: Span[] | 'off' | 'allday'; }

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const DAYS = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'];
const SOLAR = ['sunrise', 'sunset', 'dawn', 'dusk'];
const T = {
  de: { months: ['Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez'], days: ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'], daily: 'täglich', closed: 'geschlossen', allDay: 'rund um die Uhr', ph: 'Feiertage', alsoPh: 'auch an Feiertagen', sunrise: 'Sonnenaufgang', sunset: 'Sonnenuntergang', dawn: 'Morgendämmerung', dusk: 'Abenddämmerung', open: 'jetzt geöffnet', shut: 'jetzt geschlossen' },
  en: { months: MONTHS, days: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'], daily: 'daily', closed: 'closed', allDay: 'open 24 hours', ph: 'public holidays', alsoPh: 'also on public holidays', sunrise: 'sunrise', sunset: 'sunset', dawn: 'dawn', dusk: 'dusk', open: 'open now', shut: 'closed now' },
};
const idx = (list: string[], s: string) => list.findIndex(x => x.toLowerCase() === s.toLowerCase());
const TIME = /^(\d{1,2}):(\d{2})$/;
const isTimeTerm = (s: string) => SOLAR.includes(s) || (TIME.test(s) && +s.split(':')[0] <= 24 && +s.split(':')[1] < 60);

function parseMonths(tok: string): [number, number][] | null {
  const out: [number, number][] = [];
  for (const part of tok.split(',')) {
    const [a, b] = part.split('-'), m1 = idx(MONTHS, a), m2 = b === undefined ? m1 : idx(MONTHS, b);
    if (m1 < 0 || m2 < 0) return null;
    out.push([m1, m2]);
  }
  return out;
}
function parseDays(tok: string): { days: number[]; ph: boolean } | null {
  const days = new Set<number>(); let ph = false;
  for (const part of tok.split(',')) {
    if (part === 'PH') { ph = true; continue; }
    const [a, b] = part.split('-'), d1 = idx(DAYS, a), d2 = b === undefined ? d1 : idx(DAYS, b);
    if (d1 < 0 || d2 < 0) return null;
    for (let d = d1; ; d = (d + 1) % 7) { days.add(d); if (d === d2) break; }
  }
  return { days: [...days].sort((x, y) => x - y), ph };
}
function parseSpans(tok: string): Span[] | null {
  const out: Span[] = [];
  for (const part of tok.split(',')) {
    const [from, to, ...rest] = part.split('-');
    if (rest.length || !to || !isTimeTerm(from) || !isTimeTerm(to)) return null;
    out.push({ from, to });
  }
  return out;
}

/** The rules of an opening_hours value ('always' = 24/7), null when it uses syntax this parser does not know. */
export function parseOpeningHours(raw: string): Rule[] | 'always' | null {
  const text = raw.trim().replace(/,\s+/g, ',');
  if (text === '24/7') return 'always';
  const rules: Rule[] = [];
  for (const part of text.split(';')) {
    const toks = part.trim().split(/\s+/).filter(Boolean);
    if (!toks.length) continue;
    const r: Rule = { months: null, days: null, ph: false, spans: 'allday' };
    let i = 0;
    const months = parseMonths(toks[i]);
    if (months) { r.months = months; i++; }
    if (i < toks.length) { const d = parseDays(toks[i]); if (d) { r.days = d.days.length ? d.days : null; r.ph = d.ph; i++; } }
    if (i < toks.length) {
      const t = toks[i++].toLowerCase();
      if (t === 'off' || t === 'closed') r.spans = 'off';
      else { const s = parseSpans(t); if (!s) return null; r.spans = s; }
    }
    if (i < toks.length) return null;
    if (r.months === null && r.days === null && !r.ph && r.spans === 'allday') return null;
    rules.push(r);
  }
  return rules.length ? rules : null;
}

const term = (s: string, l: typeof T.de) => (SOLAR.includes(s) ? l[s as 'sunrise'] : s.replace(/^(\d):/, '0$1:'));
function dayText(days: number[], l: typeof T.de): string {
  if (days.length === 7) return l.daily;
  const runs: number[][] = [];
  for (const d of days) { const last = runs[runs.length - 1]; if (last && last[last.length - 1] === d - 1) last.push(d); else runs.push([d]); }
  return runs.map(r => (r.length >= 3 ? `${l.days[r[0]]}–${l.days[r[r.length - 1]]}` : r.map(d => l.days[d]).join(', '))).join(', ');
}
/** One human-readable line per rule (German unless lang is "en"); null when the value cannot be parsed. */
export function formatOpeningHours(raw: string, lang: string): string[] | null {
  const l = lang === 'en' ? T.en : T.de, rules = parseOpeningHours(raw);
  if (!rules) return null;
  if (rules === 'always') return [lang === 'en' ? 'Open 24/7' : 'Rund um die Uhr'];
  return rules.map(r => {
    const month = r.months ? r.months.map(([a, b]) => (a === b ? l.months[a] : `${l.months[a]}–${l.months[b]}`)).join(', ') + ': ' : '';
    const days = r.days ? dayText(r.days, l) : r.ph ? l.ph : l.daily;
    const times = r.spans === 'off' ? l.closed : r.spans === 'allday' ? l.allDay : r.spans.map(s => `${term(s.from, l)}–${term(s.to, l)}`).join(', ');
    return `${month}${days} ${times}${r.days && r.ph ? (r.spans === 'off' ? `, ${l.ph}` : `, ${l.alsoPh}`) : ''}`;
  });
}

const minutes = (s: string) => { const m = TIME.exec(s)!; return +m[1] * 60 + +m[2]; };
/** Open spans (minutes since midnight, end may exceed 1440 for overnight) of the rules on a date; null when sun times matter. */
function spansOn(rules: Rule[], date: Date): [number, number][] | null {
  const month = date.getMonth(), day = (date.getDay() + 6) % 7;
  let result: [number, number][] = [], unknown = false;
  for (const r of rules) {
    if (r.months && !r.months.some(([a, b]) => (a <= b ? month >= a && month <= b : month >= a || month <= b))) continue;
    if (r.days === null && r.ph) continue;   // ponytail: public holidays are not evaluated (no holiday calendar), a PH-only rule is ignored
    if (r.days && !r.days.includes(day)) continue;
    if (r.spans === 'off') { result = []; unknown = false; continue; }
    if (r.spans === 'allday') { result = [[0, 1440]]; unknown = false; continue; }
    if (r.spans.some(s => SOLAR.includes(s.from) || SOLAR.includes(s.to))) { unknown = true; continue; }
    unknown = false;
    result = r.spans.map(s => { const a = minutes(s.from), b = minutes(s.to); return [a, b <= a ? b + 1440 : b] as [number, number]; });
  }
  return unknown ? null : result;
}
/** Whether the place is open at `now` (local time); null when unknown (unparseable, sunrise/sunset). Public holidays are ignored. */
export function isOpenNow(raw: string, now: Date = new Date()): boolean | null {
  const rules = parseOpeningHours(raw);
  if (!rules) return null;
  if (rules === 'always') return true;
  const today = spansOn(rules, now), yesterday = spansOn(rules, new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1));
  if (today === null || yesterday === null) return null;
  const m = now.getHours() * 60 + now.getMinutes();
  return today.some(([a, b]) => m >= a && m < b) || yesterday.some(([, b]) => b > 1440 && m + 1440 < b);
}
export const openNowText = (open: boolean, lang: string) => (lang === 'en' ? T.en : T.de)[open ? 'open' : 'shut'];
