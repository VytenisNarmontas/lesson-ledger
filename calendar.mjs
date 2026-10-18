import ICAL from './vendor/package/dist/ical.js';
import { createHash } from 'node:crypto';
import { pathToFileURL } from 'node:url';

const MAX_OCCURRENCES = 100000;
const MAX_FEED_BYTES = 32 * 1024 * 1024;

export function studentName(title, marker = '[lesson]') {
  if (!marker.trim() || typeof title !== 'string' || /\[ignore\]/i.test(title)) return null;
  if (!title.toLocaleLowerCase('en').includes(marker.toLocaleLowerCase('en'))) return null;
  const escaped = marker.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const name = title.replace(new RegExp(escaped, 'gi'), '').replace(/\s+/g, ' ').trim();
  return name || null;
}

// Google normally supplies VTIMEZONE. Intl supplies IANA definitions when it doesn't.
function registerIanaZone(tzid) {
  if (ICAL.TimezoneService.has(tzid)) return;
  const formatter = new Intl.DateTimeFormat('en-GB', {
    timeZone: tzid, year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23'
  });
  const zone = new ICAL.Timezone({ tzid });
  zone.utcOffset = time => {
    const wall = Date.UTC(time.year, time.month - 1, time.day, time.hour, time.minute, time.second);
    let guess = wall;
    let offset = 0;
    for (let i = 0; i < 4; i++) {
      const parts = Object.fromEntries(formatter.formatToParts(new Date(guess)).map(p => [p.type, p.value]));
      const shown = Date.UTC(+parts.year, +parts.month - 1, +parts.day, +parts.hour, +parts.minute, +parts.second);
      offset = (shown - guess) / 1000;
      const next = wall - offset * 1000;
      if (next === guess) break;
      guess = next;
    }
    return offset;
  };
  ICAL.TimezoneService.register(zone);
}

export function expandCalendar(text, config) {
  const { marker = '[lesson]', fromEpoch, toEpoch, timezone = 'Europe/Vilnius' } = config;
  if (!marker.trim() || !Number.isFinite(fromEpoch) || !Number.isFinite(toEpoch) || toEpoch <= fromEpoch) {
    throw new Error('Invalid calendar tracking settings.');
  }
  ICAL.TimezoneService.reset();
  const calendar = new ICAL.Component(ICAL.parse(text));
  if (calendar.name !== 'vcalendar') throw new Error('This link did not return a calendar.');
  for (const zone of calendar.getAllSubcomponents('vtimezone')) ICAL.TimezoneService.register(zone);
  const components = calendar.getAllSubcomponents('vevent');
  registerIanaZone(timezone);
  for (const component of components) {
    for (const prop of component.getAllProperties()) {
      const tzid = prop.getParameter('tzid');
      if (tzid) registerIanaZone(tzid);
    }
  }
  const seconds = time => {
    if (time.zone === ICAL.Timezone.localTimezone) {
      const local = time.clone();
      local.zone = ICAL.TimezoneService.get(timezone);
      return local.toUnixTime();
    }
    return time.toUnixTime();
  };
  const results = new Map();
  const groups = new Map();
  for (const component of components) {
    const uid = component.getFirstPropertyValue('uid');
    if (!uid) continue;
    if (!groups.has(uid)) groups.set(uid, []);
    groups.get(uid).push(component);
  }
  for (const [uid, group] of groups) {
    const masters = group.filter(c => !c.hasProperty('recurrence-id'));
    const exceptions = group.filter(c => c.hasProperty('recurrence-id'));
    const add = (item, start, original, recurring, inheritedTitle) => {
      if (!start || start.isDate) return;
      if (String(item.component.getFirstPropertyValue('status') || '').toUpperCase() === 'CANCELLED') return;
      const title = item.summary || inheritedTitle || '';
      const name = studentName(title, marker);
      if (!name) return;
      const epoch = seconds(start);
      if (epoch < fromEpoch || epoch >= toEpoch) return;
      const identity = recurring ? uid + '|' + seconds(original) : uid;
      const key = createHash('sha256').update(identity).digest('hex');
      results.set(key, { Key: key, StudentName: name, Title: title, StartUtc: new Date(epoch * 1000).toISOString() });
    };
    for (const master of masters) {
      const event = new ICAL.Event(master, { exceptions, strictExceptions: true });
      // Don't expand unrelated event series, including dense reminders.
      if (!studentName(event.summary, marker) && !exceptions.some(c => studentName(c.getFirstPropertyValue('summary'), marker))) continue;
      if (String(master.getFirstPropertyValue('status') || '').toUpperCase() === 'CANCELLED') continue;
      if (!event.startDate || event.startDate.isDate) continue;
      if (!event.isRecurring()) {
        add(event, event.startDate, event.startDate, false);
        continue;
      }
      const iterator = event.iterator();
      let occurrence;
      let count = 0;
      while ((occurrence = iterator.next())) {
        if (++count > MAX_OCCURRENCES) throw new Error('A tagged recurring event is too frequent. Use ordinary lesson events.');
        // Check a generous margin for THISANDFUTURE exceptions that move dates earlier.
        if (seconds(occurrence) >= toEpoch + 366 * 86400) break;
        const override = event.exceptions[occurrence.toString()] || event.exceptions[occurrence.convertToZone(ICAL.Timezone.utcTimezone).toString()];
        if (override && String(override.component.getFirstPropertyValue('status') || '').toUpperCase() === 'CANCELLED') continue;
        const details = event.getOccurrenceDetails(occurrence);
        add(details.item, details.startDate, occurrence, true, event.summary);
      }
    }
    // A moved exception can fall into the range even when its original date doesn't.
    for (const component of exceptions) {
      const item = new ICAL.Event(component, { exceptions: [] });
      add(item, item.startDate, item.recurrenceId, true, masters[0]?.getFirstPropertyValue('summary'));
    }
  }
  return [...results.values()].sort((a, b) => a.StartUtc.localeCompare(b.StartUtc) || a.Key.localeCompare(b.Key));
}

export function validateFeedUrl(value) {
  const url = new URL(value);
  if (url.protocol !== 'https:' || url.hostname !== 'calendar.google.com' ||
      !url.pathname.startsWith('/calendar/ical/') || !url.pathname.endsWith('/basic.ics') ||
      url.username || url.password || url.port || url.hash || url.search) {
    throw new Error('Paste the Google Calendar Secret address in iCal format.');
  }
  return url;
}

export async function downloadCalendar(url) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 25000);
  try {
    const response = await fetch(validateFeedUrl(url), { signal: controller.signal, redirect: 'error' });
    if (!response.ok) throw new Error('Google Calendar could not be read. Check your calendar link.');
    let size = 0;
    const chunks = [];
    for await (const chunk of response.body) {
      size += chunk.length;
      if (size > MAX_FEED_BYTES) throw new Error('This calendar is too large to sync.');
      chunks.push(chunk);
    }
    return Buffer.concat(chunks).toString('utf8');
  } finally { clearTimeout(timeout); }
}

async function main() {
  let input = '';
  for await (const chunk of process.stdin) {
    input += chunk;
    if (input.length > 65536) throw new Error('Calendar settings are too large.');
  }
  const config = JSON.parse(input);
  const text = await downloadCalendar(config.url);
  process.stdout.write(JSON.stringify({ Events: expandCalendar(text, config), SyncedUtc: new Date().toISOString() }));
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch(error => {
    // Never expose the private feed address in a stack trace or log.
    const safe = /^(Invalid calendar|This link|This calendar|Google Calendar|Paste the|A tagged)/.test(error.message);
    process.stderr.write(safe ? error.message : 'Calendar sync failed. Check your connection and calendar link.');
    process.exitCode = 1;
  });
}
