import { test } from 'node:test';
import assert from 'node:assert/strict';
import { expandCalendar, studentName, validateFeedUrl } from './calendar.mjs';

const wrap = (...events) => ['BEGIN:VCALENDAR', 'VERSION:2.0', 'PRODID:-//LessonLedger QA//EN', ...events, 'END:VCALENDAR'].join('\r\n');
const event = (uid, summary, start, extra = [], end) => ['BEGIN:VEVENT', `UID:${uid}`, `SUMMARY:${summary}`, `DTSTART${start}`, end ? `DTEND${end}` : 'DURATION:PT1H', ...extra, 'END:VEVENT'].join('\r\n');
const config = { marker: '[lesson]', timezone: 'Europe/Vilnius', fromEpoch: Date.parse('2026-10-01T21:00:00Z') / 1000, toEpoch: Date.parse('2026-11-10T00:00:00Z') / 1000 };

test('only tagged private names match', () => {
  assert.equal(studentName('ALEX'), null);
  assert.equal(studentName('[lesson] ALEX'), 'ALEX');
  assert.equal(studentName('Alex [LESSON]'), 'Alex');
  assert.equal(studentName('[lesson] MAT - 8 - Jordan | [ignore]'), null);
  assert.equal(studentName('[lesson]'), null);
  assert.equal(studentName('#private Jamie', '#private'), 'Jamie');
});
test('tracking starts at Vilnius midnight; future/all-day/unmarked/cancelled ignored', () => {
  const output = expandCalendar(wrap(
    event('old', '[lesson] ALEX', ':20261001T205959Z'),
    event('start', '[lesson] ALEX', ':20261001T210000Z'),
    event('day', '[lesson] JAMIE', ';VALUE=DATE:20261002'),
    event('future', '[lesson] MORGAN', ':20261111T170000Z'),
    event('none', 'ALEX', ':20261002T170000Z'),
    event('cancelled', '[lesson] CASEY', ':20261002T170000Z', ['STATUS:CANCELLED'])
  ), config);
  assert.equal(output.length, 1);
  assert.equal(output[0].StartUtc, '2026-10-01T21:00:00.000Z');
});
test('weekly recurrence catches up and DST preserves the local lesson time', () => {
  const output = expandCalendar(wrap(event('weekly', '[lesson] JAMIE', ';TZID=Europe/Vilnius:20260924T190000', ['RRULE:FREQ=WEEKLY;COUNT=7'])), config);
  assert.equal(output.length, 5);
  assert.equal(output[0].StartUtc, '2026-10-08T16:00:00.000Z');
  assert.equal(output[3].StartUtc, '2026-10-29T17:00:00.000Z');
  assert.equal(new Set(output.map(x => x.Key)).size, 5);
});
test('rescheduling a single event preserves its deduplication key', () => {
  const first = expandCalendar(wrap(event('single', '[lesson] ALEX', ':20261002T170000Z')), config);
  const moved = expandCalendar(wrap(event('single', '[lesson] ALEX', ':20261003T170000Z')), config);
  assert.equal(first[0].Key, moved[0].Key);
});
test('moved and cancelled instances are handled without polluting another student series', () => {
  const output = expandCalendar(wrap(
    event('alex', '[lesson] ALEX', ':20261002T170000Z', ['RRULE:FREQ=WEEKLY;COUNT=3']),
    event('alex', '[lesson] ALEX', ':20261010T180000Z', ['RECURRENCE-ID:20261009T170000Z']),
    event('alex', '[lesson] ALEX', ':20261016T170000Z', ['RECURRENCE-ID:20261016T170000Z', 'STATUS:CANCELLED']),
    event('jamie', '[lesson] JAMIE', ':20261002T170000Z', ['RRULE:FREQ=WEEKLY;COUNT=3'])
  ), config);
  assert.equal(output.length, 5);
  assert.equal(output.filter(e => e.StudentName === 'JAMIE').length, 3);
  assert.equal(output.filter(e => e.StudentName === 'ALEX').length, 2);
  const moved = output.find(e => e.StudentName === 'ALEX' && e.StartUtc.includes('10-10'));
  const original = expandCalendar(wrap(event('alex', '[lesson] ALEX', ':20261002T170000Z', ['RRULE:FREQ=WEEKLY;COUNT=3'])), config).find(e => e.StartUtc.includes('10-09'));
  assert.equal(moved.Key, original.Key);
});
test('EXDATE removes a cancelled recurring lesson', () => {
  const output = expandCalendar(wrap(event('ex', '[lesson] CASEY', ':20261002T170000Z', ['RRULE:FREQ=WEEKLY;COUNT=3', 'EXDATE:20261009T170000Z'])), config);
  assert.equal(output.length, 2);
});
test('minimal cancellation exceptions without DTSTART are ignored safely', () => {
  const cancelled = ['BEGIN:VEVENT', 'UID:minimal', 'RECURRENCE-ID:20261009T170000Z', 'STATUS:CANCELLED', 'END:VEVENT'].join('\r\n');
  const output = expandCalendar(wrap(event('minimal', '[lesson] ALEX', ':20261002T170000Z', ['RRULE:FREQ=WEEKLY;COUNT=3']), cancelled), config);
  assert.equal(output.length, 2);
});
test('an exception moved into the tracking interval from a later original date is found', () => {
  const output = expandCalendar(wrap(
    event('move', '[lesson] TAYLOR', ':20261112T170000Z', ['RRULE:FREQ=WEEKLY;COUNT=2']),
    event('move', '[lesson] TAYLOR', ':20261003T170000Z', ['RECURRENCE-ID:20261112T170000Z'])
  ), config);
  assert.equal(output.length, 1);
  assert.equal(output[0].StartUtc, '2026-10-03T17:00:00.000Z');
});
test('unrelated dense series do not prevent syncing tutoring lessons', () => {
  const output = expandCalendar(wrap(
    event('dense', 'Drink water', ':20000101T000000Z', ['RRULE:FREQ=SECONDLY']),
    event('lesson', '[lesson] ALEX', ':20261002T170000Z')
  ), config);
  assert.equal(output.length, 1);
});
test('feed is restricted to Google HTTPS iCal addresses', () => {
  assert.equal(validateFeedUrl('https://calendar.google.com/calendar/ical/example/private-example/basic.ics').hostname, 'calendar.google.com');
  for (const url of ['http://calendar.google.com/calendar/ical/a/basic.ics', 'https://evil.example/basic.ics', 'https://calendar.google.com/calendar/ical/a/basic.ics?q=secret', 'https://calendar.google.com.evil.example/calendar/ical/a/basic.ics']) assert.throws(() => validateFeedUrl(url));
});
