# Third-party notices

The project's C#, calendar integration, scripts, and documentation are offered under the [MIT license](LICENSE).

## ical.js 2.2.1

Lesson Ledger uses [ical.js](https://github.com/kewisch/ical.js), by Philipp Kewisch and contributors, to parse iCalendar data.

The unchanged upstream package is included in `vendor/package`, including its preferred source files under `lib/ical`, distributions under `dist`, package metadata, README, and the [Mozilla Public License 2.0](vendor/package/LICENSE). These files remain under MPL-2.0; the project's MIT license does not replace their license. Original notices are retained.

## Node.js

Node.js is required separately for the calendar helper. Its executable is not distributed in this repository. Obtain it from the [official Node.js website](https://nodejs.org/en/download) with its original license and notices. A local copy in `vendor/node.exe` is ignored by Git.

## Windows and .NET Framework

Windows and the .NET Framework build/runtime components are separate requirements. They are not included in this repository.
