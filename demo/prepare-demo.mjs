// Fictional sample data only. This does not open or copy the real Data folder.
import { mkdir, writeFile, access } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { join, dirname } from 'node:path';

const folder = join(dirname(fileURLToPath(import.meta.url)), 'data');
try {
  await access(join(folder, 'ledger.json'));
  if (!process.argv.includes('--reset')) throw new Error('Demo records already exist. Use --reset to replace only the fictional demo records.');
} catch (error) {
  if (error.code !== 'ENOENT') throw error;
}
const now = Date.now();
const day = new Intl.DateTimeFormat('en-CA', { timeZone:'Europe/Vilnius', year:'numeric', month:'2-digit', day:'2-digit' }).format(new Date(now-48*3600000));
const at = hoursAgo => new Date(now-hoursAgo*3600000).toISOString();
const state = { Schema:1, Sequence:0, Settings:{Marker:'[lesson]',TrackingDate:day,Currency:'EUR',Monitor:-1}, Students:[],Entries:[],Payments:[] };
for (const [name,price,count,amount] of [['Alex',31.5,2,63],['Jamie',42,1,42],['Morgan',27.5,3,82.5],['Taylor',38.75,0,0]]) {
  const student = {Id:'demo-'+name.toLowerCase(),Name:name,CalendarName:name.toUpperCase(),Price:price,Active:true};
  state.Students.push(student);
  if (count) state.Entries.push({Id:'opening-'+name,StudentId:student.Id,EventKey:null,Title:'Fictional opening balance',TimeUtc:at(24),Kind:'adjustment',Count:count,Amount:amount,PaidBy:null,Sequence:++state.Sequence});
}
const cache = {Events:[{Key:'demo-alex-lesson',StudentName:'ALEX',Title:'[lesson] ALEX',StartUtc:at(0.1)}],SyncedUtc:new Date(now).toISOString()};
await mkdir(folder,{recursive:true});
await writeFile(join(folder,'ledger.json'),JSON.stringify(state,null,2));
await writeFile(join(folder,'calendar.json'),JSON.stringify(cache,null,2));
console.log('Created fictional demo: Alex, Jamie, Morgan and Taylor. No calendar link or live data is used.');
