using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LessonLedger {
  public class Student {
    public string Id { get; set; }
    public string Name { get; set; }
    public string CalendarName { get; set; }
    public decimal Price { get; set; }
    public bool Active { get; set; }
    public Student() { Id = Guid.NewGuid().ToString("N"); Active = true; }
  }
  public class Entry {
    public string Id { get; set; }
    public string StudentId { get; set; }
    public string EventKey { get; set; }
    public string Title { get; set; }
    public string TimeUtc { get; set; }
    public string Kind { get; set; }
    public int Count { get; set; }
    public decimal Amount { get; set; }
    public string PaidBy { get; set; }
    public long Sequence { get; set; }
    public Entry() { Id = Guid.NewGuid().ToString("N"); }
  }
  public class Payment {
    public string Id { get; set; }
    public string StudentId { get; set; }
    public string TimeUtc { get; set; }
    public decimal Amount { get; set; }
    public int Count { get; set; }
    public long Sequence { get; set; }
    public bool Undone { get; set; }
    public Payment() { Id = Guid.NewGuid().ToString("N"); }
  }
  public class Settings {
    public string Marker { get; set; }
    public string TrackingDate { get; set; }
    public string Currency { get; set; }
    public int Monitor { get; set; }
    public Settings() { Marker = "[lesson]"; TrackingDate = "2026-10-02"; Currency = "EUR"; Monitor = -1; }
  }
  public class State {
    public int Schema { get; set; }
    public long Sequence { get; set; }
    public Settings Settings { get; set; }
    public List<Student> Students { get; set; }
    public List<Entry> Entries { get; set; }
    public List<Payment> Payments { get; set; }
    public State() { Schema = 1; Settings = new Settings(); Students = new List<Student>(); Entries = new List<Entry>(); Payments = new List<Payment>(); }
  }
  public class ScheduledLesson {
    public string Key { get; set; }
    public string StudentName { get; set; }
    public string Title { get; set; }
    public string StartUtc { get; set; }
  }
  public class CalendarCache {
    public List<ScheduledLesson> Events { get; set; }
    public string SyncedUtc { get; set; }
    public CalendarCache() { Events = new List<ScheduledLesson>(); }
  }
  public class UpcomingLesson {
    public string Key { get; set; }
    public DateTime StartUtc { get; set; }
    public string StudentName { get; set; }
    public string CalendarTitle { get; set; }
    public decimal? ExpectedPay { get; set; }
    public string PriceNote { get; set; }
  }
  public static class UpcomingLessons {
    public static List<UpcomingLesson> Build(Ledger ledger, CalendarCache cache, DateTime nowUtc) {
      DateTime from = Time.TrackingStart(ledger.Data.Settings);
      if (nowUtc > from) from = nowUtc;
      var rows = new List<UpcomingLesson>();
      foreach (var lesson in cache == null || cache.Events == null ? new List<ScheduledLesson>() : cache.Events) {
        DateTime start = Time.Utc(lesson.StartUtc);
        if (start < from) continue;
        var student = ledger.Match(lesson.StudentName);
        rows.Add(new UpcomingLesson { Key = lesson.Key, StartUtc = start,
          StudentName = student == null ? lesson.StudentName : student.Name, CalendarTitle = lesson.Title,
          ExpectedPay = student != null && student.Price > 0 ? (decimal?)student.Price : null,
          PriceNote = student == null ? "Student not added" : student.Price <= 0 ? "Price not set" : "" });
      }
      return rows.OrderBy(r => r.StartUtc).ThenBy(r => r.StudentName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Key, StringComparer.Ordinal).ToList();
    }
  }
  public static class Json {
    public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 }; }
    public static string Write(object value) { return Serializer().Serialize(value); }
    public static T Read<T>(string value) { return Serializer().Deserialize<T>(value); }
  }
  public static class Time {
    public static TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");
    public static DateTime Utc(string value) { return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal); }
    public static DateTime Local(string value) { return TimeZoneInfo.ConvertTimeFromUtc(Utc(value), Zone); }
    public static DateTime TrackingStart(Settings settings) { return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(DateTime.ParseExact(settings.TrackingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Unspecified), Zone); }
    public static long Epoch(DateTime value) { return (long)(value.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }
    public static string Now() { return DateTime.UtcNow.ToString("o"); }
  }
  public class Ledger {
    public State Data;
    public Ledger(State data) { Data = data; }
    public static string NameKey(string name) { return (name ?? "").Trim().ToUpperInvariant(); }
    public Student Match(string name) { return Data.Students.FirstOrDefault(s => s.Active && NameKey(s.CalendarName) == NameKey(name)); }
    public int Count(Student student) { return Data.Entries.Where(e => e.StudentId == student.Id && e.PaidBy == null).Sum(e => e.Count); }
    public decimal Balance(Student student) { return Data.Entries.Where(e => e.StudentId == student.Id && e.PaidBy == null).Sum(e => e.Amount); }
    public bool Answered(string key) { return Data.Entries.Any(e => e.EventKey == key); }
    public void Answer(ScheduledLesson lesson, Student student, bool yes) {
      if (Answered(lesson.Key)) return;
      if (yes && student.Price <= 0) throw new InvalidOperationException("Set this student's lesson price first.");
      Data.Entries.Add(new Entry { StudentId = student.Id, EventKey = lesson.Key, Title = lesson.Title,
        TimeUtc = lesson.StartUtc, Kind = yes ? "lesson" : "skipped", Count = yes ? 1 : 0,
        Amount = yes ? student.Price : 0, Sequence = ++Data.Sequence });
    }
    public void SetTotals(Student student, int count, decimal amount) {
      if (count < 0 || amount < 0) throw new InvalidOperationException("Counts and balances must be zero or greater.");
      int deltaCount = count - Count(student); decimal deltaAmount = amount - Balance(student);
      if (deltaCount == 0 && deltaAmount == 0) return;
      Data.Entries.Add(new Entry { StudentId = student.Id, TimeUtc = Time.Now(), Kind = "adjustment", Title = "Balance correction / opening balance",
        Count = deltaCount, Amount = deltaAmount, Sequence = ++Data.Sequence });
    }
    public Payment Pay(Student student) {
      int count = Count(student); decimal balance = Balance(student);
      if (count == 0 && balance == 0) return null;
      var payment = new Payment { StudentId = student.Id, TimeUtc = Time.Now(), Count = count, Amount = balance, Sequence = ++Data.Sequence };
      foreach (var entry in Data.Entries.Where(e => e.StudentId == student.Id && e.PaidBy == null && e.Kind != "skipped")) entry.PaidBy = payment.Id;
      Data.Payments.Add(payment); return payment;
    }
    public bool CanUndo(Payment payment) {
      if (payment == null || payment.Undone) return false;
      return !Data.Entries.Any(e => e.StudentId == payment.StudentId && e.Sequence > payment.Sequence) &&
        !Data.Payments.Any(p => p.StudentId == payment.StudentId && p.Sequence > payment.Sequence && !p.Undone);
    }
    public void Undo(Payment payment) {
      if (!CanUndo(payment)) throw new InvalidOperationException("This student has newer changes. Use Edit to correct their balance.");
      foreach (var entry in Data.Entries.Where(e => e.PaidBy == payment.Id)) entry.PaidBy = null;
      payment.Undone = true; ++Data.Sequence;
    }
  }
  public class Store {
    public readonly string Folder;
    public string FilePath { get { return Path.Combine(Folder, "ledger.json"); } }
    public Store(string folder) { Folder = folder; Directory.CreateDirectory(Folder); }
    public State Load() {
      if (!File.Exists(FilePath)) return new State();
      try { var state = Json.Read<State>(File.ReadAllText(FilePath, Encoding.UTF8)); Validate(state); return state; }
      catch (Exception exception) { throw new InvalidDataException("Your saved records could not be opened. They have been preserved. A backup is in the Data folder.", exception); }
    }
    static void Validate(State state) {
      if (state == null || state.Schema != 1 || state.Settings == null || state.Students == null || state.Entries == null || state.Payments == null) throw new InvalidDataException("Invalid records.");
      if (String.IsNullOrWhiteSpace(state.Settings.Marker)) throw new InvalidDataException("Missing lesson marker.");
      Time.TrackingStart(state.Settings);
      if (state.Students.Select(s => s.Id).Distinct().Count() != state.Students.Count) throw new InvalidDataException("Duplicate students.");
      if (state.Entries.Where(e => e.EventKey != null).Select(e => e.EventKey).Distinct().Count() != state.Entries.Count(e => e.EventKey != null)) throw new InvalidDataException("Duplicate lessons.");
    }
    public void Save(State state) { Validate(state); AtomicWrite(FilePath, Json.Write(state), true); }
    public static void AtomicWrite(string path, string value, bool backup) {
      string temporary = path + ".tmp";
      using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) {
        byte[] bytes = new UTF8Encoding(false).GetBytes(value); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
      }
      if (File.Exists(path)) File.Replace(temporary, path, backup ? path + ".bak" : null); else File.Move(temporary, path);
    }
    public CalendarCache LoadCache() {
      try { string path = Path.Combine(Folder, "calendar.json"); return File.Exists(path) ? Json.Read<CalendarCache>(File.ReadAllText(path)) : new CalendarCache(); }
      catch { return new CalendarCache(); }
    }
    public string GetFeed() {
      string path = Path.Combine(Folder, "calendar-link.dat");
      if (!File.Exists(path)) return "";
      return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser));
    }
    public void SetFeed(string value) {
      string path = Path.Combine(Folder, "calendar-link.dat");
      if (value.Length == 0) { if (File.Exists(path)) File.Delete(path); return; }
      byte[] protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
      string temporary = path + ".tmp"; File.WriteAllBytes(temporary, protectedBytes);
      if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
    }
  }
  public static class Startup {
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "LessonLedger";
    static string UserId { get { return System.Security.Principal.WindowsIdentity.GetCurrent().User.Value; } }
    public static string TaskName { get { return "LessonLedger-Desktop-" + UserId; } }
    static bool CurrentUser(string account) {
      if (account == UserId) return true;
      try { return new System.Security.Principal.NTAccount(account).Translate(typeof(System.Security.Principal.SecurityIdentifier)).Value == UserId; }
      catch { return false; }
    }
    static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    static dynamic Connect() {
      dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
      service.Connect(); return service;
    }
    static dynamic Find(dynamic folder) {
      try { return folder.GetTask(TaskName); }
      catch (COMException exception) { if ((uint)exception.ErrorCode == 0x80070002) return null; throw; }
    }
    public static bool Enabled() {
      dynamic service = null, folder = null, task = null, definition = null, action = null, trigger = null;
      try {
        service = Connect(); folder = service.GetFolder(@"\"); task = Find(folder);
        if (task == null || !task.Enabled) return false;
        definition = task.Definition;
        if (definition.Actions.Count != 1 || definition.Triggers.Count != 1) return false;
        action = definition.Actions.Item(1); trigger = definition.Triggers.Item(1);
        return definition.Principal.LogonType == 3 && CurrentUser((string)definition.Principal.UserId) &&
          trigger.Type == 9 && trigger.Enabled && CurrentUser((string)trigger.UserId) &&
          String.Equals((string)action.Path, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) && action.Arguments == "--startup";
      } catch { return false; }
      finally { Release(trigger); Release(action); Release(definition); Release(task); Release(folder); Release(service); }
    }
    public static void Set(bool enabled) {
      dynamic service = null, folder = null, task = null, definition = null;
      try {
        service = Connect(); folder = service.GetFolder(@"\");
        if (enabled) {
          definition = service.NewTask(0);
          definition.RegistrationInfo.Description = "Start the Lesson Ledger desktop widget when this user signs in.";
          definition.Principal.UserId = UserId;
          definition.Principal.LogonType = 3; // The signed-in user's interactive desktop; no stored password.
          definition.Principal.RunLevel = 0;
          dynamic settings = definition.Settings;
          settings.Enabled = true; settings.AllowDemandStart = true; settings.StartWhenAvailable = true;
          settings.DisallowStartIfOnBatteries = false; settings.StopIfGoingOnBatteries = false;
          settings.ExecutionTimeLimit = "PT0S"; settings.MultipleInstances = 2;
          settings.RestartInterval = "PT1M"; settings.RestartCount = 3;
          dynamic trigger = definition.Triggers.Create(9);
          trigger.UserId = UserId; trigger.Enabled = true;
          dynamic action = definition.Actions.Create(0);
          action.Path = Application.ExecutablePath; action.Arguments = "--startup";
          action.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
          task = folder.RegisterTaskDefinition(TaskName, definition, 6, UserId, null, 3, null);
          if (!Enabled()) throw new InvalidOperationException("Windows did not enable the startup task.");
        } else {
          task = Find(folder); if (task != null) task.Enabled = false;
        }
        // Retire the old Run entry only after the replacement has been registered.
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (key != null) key.DeleteValue(ValueName, false);
      } finally {
        Release(task); Release(definition); Release(folder); Release(service);
      }
    }
  }
  public static class Native {
    public delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] public static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
    public static void SetOwner(IntPtr window, IntPtr owner) {
      if (IntPtr.Size == 8) SetWindowLongPtr64(window, -8, owner); else SetWindowLong(window, -8, owner.ToInt32());
    }
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr window, ref POINT point);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rectangle);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
    public struct POINT { public int X; public int Y; public POINT(int x, int y) { X = x; Y = y; } }
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    public static string ClassName(IntPtr window) { var name = new StringBuilder(128); GetClassName(window, name, name.Capacity); return name.ToString(); }
    public static IntPtr DesktopHost() {
      IntPtr host = IntPtr.Zero;
      EnumWindows(delegate(IntPtr window, IntPtr parameter) {
        if (FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) { host = window; return false; }
        return true;
      }, IntPtr.Zero);
      return host;
    }
    public static IntPtr WindowAboveDesktop(IntPtr desktop, IntPtr widget) {
      IntPtr previous = IntPtr.Zero;
      EnumWindows(delegate(IntPtr window, IntPtr parameter) {
        if (window == desktop) return false;
        if (window != widget && IsWindowVisible(window) && !IsIconic(window)) previous = window;
        return true;
      }, IntPtr.Zero);
      return previous;
    }
    public static IntPtr VisibleNeighbor(IntPtr window, uint direction) {
      for (int i = 0; i < 10000; i++) {
        window = GetWindow(window, direction);
        if (window == IntPtr.Zero || (IsWindowVisible(window) && !IsIconic(window))) return window;
      }
      return IntPtr.Zero;
    }
  }
  public static class Theme {
    public static readonly Color Background = Color.FromArgb(20, 25, 34);
    public static readonly Color Card = Color.FromArgb(31, 38, 49);
    public static readonly Color Light = Color.FromArgb(237, 242, 249);
    public static readonly Color Muted = Color.FromArgb(154, 167, 186);
    public static readonly Color Accent = Color.FromArgb(115, 218, 193);
    public static readonly Color Amber = Color.FromArgb(255, 207, 125);
    public static Font Font(float size, FontStyle style) { return new Font("Segoe UI", size, style, GraphicsUnit.Point); }
    public static Label Label(string text, int x, int y, int width, int height, float size, Color color, FontStyle style) {
      return new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), ForeColor = color, Font = Font(size, style), BackColor = Color.Transparent, AutoEllipsis = true };
    }
    public static Button Button(string text, int x, int y, int width, int height, bool primary) {
      var button = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, height), FlatStyle = FlatStyle.Flat,
        Font = Font(9, FontStyle.Bold), ForeColor = primary ? Background : Light, BackColor = primary ? Accent : Card,
        Cursor = Cursors.Hand, TabStop = true, UseVisualStyleBackColor = false };
      button.FlatAppearance.BorderSize = 0; return button;
    }
    public static TextBox TextBox(int x, int y, int width, string value) {
      return new TextBox { Location = new Point(x, y), Width = width, Text = value, Font = Font(10, FontStyle.Regular), BorderStyle = BorderStyle.FixedSingle };
    }
    public static NumericUpDown Number(int x, int y, int width, decimal value, int decimals) {
      return new NumericUpDown { Location = new Point(x, y), Width = width, Value = Math.Max(0, Math.Min(1000000, value)), Minimum = 0,
        Maximum = 1000000, DecimalPlaces = decimals, ThousandsSeparator = true, Font = Font(10, FontStyle.Regular), BorderStyle = BorderStyle.FixedSingle };
    }
    public static void Dialog(Form form, string title, int width, int height) {
      form.Text = title; form.ClientSize = new Size(width, height); form.BackColor = Background; form.ForeColor = Light;
      form.Font = Font(10, FontStyle.Regular); form.FormBorderStyle = FormBorderStyle.FixedDialog; form.MaximizeBox = false;
      form.MinimizeBox = false; form.StartPosition = FormStartPosition.CenterScreen; form.ShowInTaskbar = true;
    }
  }
  public class StudentDialog : Form {
    TextBox name, calendarName; NumericUpDown price, count, amount; Label error;
    public string StudentName { get { return name.Text.Trim(); } }
    public string CalendarName { get { return calendarName.Text.Trim(); } }
    public decimal Price { get { return price.Value; } }
    public int LessonCount { get { return (int)count.Value; } }
    public decimal Amount { get { return amount.Value; } }
    public StudentDialog(Student student, int existingCount, decimal existingAmount, string currency) {
      Theme.Dialog(this, student == null ? "Add student" : "Edit " + student.Name, 430, 448);
      Controls.Add(Theme.Label("STUDENT DETAILS", 24, 22, 380, 24, 10, Theme.Accent, FontStyle.Bold));
      Controls.Add(Theme.Label("Display name", 24, 62, 180, 24, 9, Theme.Muted, FontStyle.Regular));
      name = Theme.TextBox(24, 87, 380, student == null ? "" : student.Name); Controls.Add(name);
      Controls.Add(Theme.Label("Calendar name (without the marker)", 24, 123, 380, 24, 9, Theme.Muted, FontStyle.Regular));
      calendarName = Theme.TextBox(24, 148, 380, student == null ? "" : student.CalendarName); Controls.Add(calendarName);
      Controls.Add(Theme.Label("Price per lesson (" + currency + ")", 24, 185, 190, 24, 9, Theme.Muted, FontStyle.Regular));
      price = Theme.Number(24, 210, 180, student == null ? 0 : student.Price, 2); Controls.Add(price);
      Controls.Add(Theme.Label("CURRENT UNPAID BALANCE", 24, 254, 380, 24, 10, Theme.Accent, FontStyle.Bold));
      Controls.Add(Theme.Label("Unpaid lessons", 24, 287, 180, 22, 9, Theme.Muted, FontStyle.Regular));
      Controls.Add(Theme.Label("Amount owed (" + currency + ")", 224, 287, 180, 22, 9, Theme.Muted, FontStyle.Regular));
      count = Theme.Number(24, 312, 180, existingCount, 0); amount = Theme.Number(224, 312, 180, existingAmount, 2);
      Controls.Add(count); Controls.Add(amount);
      error = Theme.Label("Enter an existing balance here. Price changes affect future lessons.", 24, 350, 380, 38, 8.5f, Theme.Muted, FontStyle.Regular); Controls.Add(error);
      var cancel = Theme.Button("Cancel", 224, 394, 80, 32, false); cancel.DialogResult = DialogResult.Cancel; Controls.Add(cancel); CancelButton = cancel;
      var save = Theme.Button("Save", 314, 394, 90, 32, true); Controls.Add(save); AcceptButton = save;
      save.Click += delegate {
        if (StudentName.Length == 0) { error.Text = "Enter a student name."; return; }
        if (calendarName.Text.Trim().Length == 0) calendarName.Text = StudentName;
        if (StudentName.Length > 100 || CalendarName.Length > 100) { error.Text = "Use a shorter student name (100 characters maximum)."; return; }
        DialogResult = DialogResult.OK; Close();
      };
    }
  }
  public class SettingsDialog : Form {
    TextBox feed, marker, currency; DateTimePicker date; CheckBox startup; ComboBox monitor; Label error;
    public string Feed { get { return feed.Text.Trim(); } }
    public string Marker { get { return marker.Text.Trim(); } }
    public string Currency { get { return currency.Text.Trim().ToUpperInvariant(); } }
    public string TrackingDate { get { return date.Value.ToString("yyyy-MM-dd"); } }
    public bool AutoStart { get { return startup.Checked; } }
    public int MonitorIndex { get { return monitor.SelectedIndex; } }
    public SettingsDialog(Settings settings, string existingFeed) {
      Theme.Dialog(this, "Lesson Ledger settings", 510, 543);
      Controls.Add(Theme.Label("GOOGLE CALENDAR", 24, 22, 460, 24, 10, Theme.Accent, FontStyle.Bold));
      Controls.Add(Theme.Label("Secret address in iCal format", 24, 57, 460, 24, 9, Theme.Muted, FontStyle.Regular));
      feed = Theme.TextBox(24, 82, 460, existingFeed); feed.UseSystemPasswordChar = true; Controls.Add(feed);
      var show = new CheckBox { Text = "Show link", Location = new Point(24, 116), Size = new Size(130, 23), ForeColor = Theme.Muted, Font = Theme.Font(9, FontStyle.Regular) };
      show.CheckedChanged += delegate { feed.UseSystemPasswordChar = !show.Checked; }; Controls.Add(show);
      var help = new LinkLabel { Text = "Where to find this in Google Calendar", Location = new Point(174, 118), Size = new Size(310, 23), LinkColor = Theme.Accent, ActiveLinkColor = Theme.Light, Font = Theme.Font(9, FontStyle.Regular) };
      help.LinkClicked += delegate { Process.Start("https://support.google.com/calendar/answer/37648"); }; Controls.Add(help);
      Controls.Add(Theme.Label("Calendar Settings > your calendar > Integrate calendar.\nPaste its Secret address here. Your calendar stays read-only.", 24, 151, 460, 46, 9, Theme.Muted, FontStyle.Regular));
      Controls.Add(Theme.Label("Event title marker", 24, 210, 215, 24, 9, Theme.Muted, FontStyle.Regular));
      Controls.Add(Theme.Label("Currency", 269, 210, 215, 24, 9, Theme.Muted, FontStyle.Regular));
      marker = Theme.TextBox(24, 235, 215, settings.Marker); currency = Theme.TextBox(269, 235, 215, settings.Currency); Controls.Add(marker); Controls.Add(currency);
      Controls.Add(Theme.Label("Example: [lesson] ALEX. [ignore] events are ignored.", 24, 270, 460, 25, 9, Theme.Muted, FontStyle.Regular));
      Controls.Add(Theme.Label("Track from (Vilnius time)", 24, 309, 215, 24, 9, Theme.Muted, FontStyle.Regular));
      Controls.Add(Theme.Label("Desktop monitor", 269, 309, 215, 24, 9, Theme.Muted, FontStyle.Regular));
      date = new DateTimePicker { Location = new Point(24, 334), Size = new Size(215, 26), Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Value = DateTime.ParseExact(settings.TrackingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture) }; Controls.Add(date);
      monitor = new ComboBox { Location = new Point(269, 334), Size = new Size(215, 26), DropDownStyle = ComboBoxStyle.DropDownList };
      for (int i = 0; i < Screen.AllScreens.Length; i++) monitor.Items.Add("Monitor " + (i + 1) + (Screen.AllScreens[i].Primary ? " (main)" : ""));
      monitor.SelectedIndex = settings.Monitor < 0 ? Array.FindIndex(Screen.AllScreens, s => s.Primary) : Math.Max(0, Math.Min(settings.Monitor, monitor.Items.Count - 1)); Controls.Add(monitor);
      startup = new CheckBox { Text = "Start automatically with Windows", Location = new Point(24, 387), Size = new Size(460, 26), ForeColor = Theme.Light, Checked = Startup.Enabled() }; Controls.Add(startup);
      error = Theme.Label("Sync checks every 5 minutes. Due questions stay in the widget.\nCurrency changes only change the label, not recorded amounts.", 24, 425, 460, 46, 9, Theme.Muted, FontStyle.Regular); Controls.Add(error);
      var cancel = Theme.Button("Cancel", 294, 489, 90, 32, false); cancel.DialogResult = DialogResult.Cancel; Controls.Add(cancel); CancelButton = cancel;
      var save = Theme.Button("Save", 394, 489, 90, 32, true); Controls.Add(save); AcceptButton = save;
      save.Click += delegate {
        if (Marker.Length == 0 || Marker.Length > 40) { error.Text = "Enter a marker between 1 and 40 characters."; return; }
        if (Currency.Length == 0 || Currency.Length > 5) { error.Text = "Enter a currency such as EUR."; return; }
        if (Feed.Length > 0) {
          Uri url;
          if (!Uri.TryCreate(Feed, UriKind.Absolute, out url) || url.Scheme != "https" || url.Host != "calendar.google.com" ||
              !url.AbsolutePath.StartsWith("/calendar/ical/", StringComparison.Ordinal) || !url.AbsolutePath.EndsWith("/basic.ics", StringComparison.Ordinal) ||
              url.UserInfo.Length > 0 || !url.IsDefaultPort || url.Query.Length > 0 || url.Fragment.Length > 0) {
            error.Text = "Paste the Google Calendar Secret address in iCal format."; return;
          }
        }
        DialogResult = DialogResult.OK; Close();
      };
    }
  }
  public class HistoryDialog : Form {
    Ledger ledger; ListView list; Button undo;
    public Payment SelectedPayment;
    public HistoryDialog(Ledger current, string currency) {
      ledger = current; Theme.Dialog(this, "Lesson and payment history", 740, 466);
      Controls.Add(Theme.Label("LESSONS & PAYMENTS", 20, 18, 690, 28, 11, Theme.Accent, FontStyle.Bold));
      list = new ListView { Location = new Point(20, 57), Size = new Size(700, 335), View = View.Details, FullRowSelect = true, MultiSelect = false,
        BackColor = Theme.Card, ForeColor = Theme.Light, BorderStyle = BorderStyle.None, HideSelection = false };
      list.Columns.Add("Date", 130); list.Columns.Add("Student", 150); list.Columns.Add("Action", 180); list.Columns.Add("Lessons", 70); list.Columns.Add("Amount", 140);
      var rows = new List<Tuple<long, ListViewItem>>();
      foreach (var entry in ledger.Data.Entries) {
        var student = ledger.Data.Students.FirstOrDefault(s => s.Id == entry.StudentId);
        string action = entry.Kind == "lesson" ? (entry.PaidBy == null ? "Lesson (unpaid)" : "Lesson (paid)") : entry.Kind == "skipped" ? "Did not happen" : "Balance correction";
        var item = new ListViewItem(new string[] { Time.Local(entry.TimeUtc).ToString("dd MMM yyyy HH:mm"), student == null ? "Student" : student.Name, action, entry.Count.ToString(), Money(entry.Amount, currency) });
        rows.Add(Tuple.Create(entry.Sequence, item));
      }
      foreach (var payment in ledger.Data.Payments) {
        var student = ledger.Data.Students.FirstOrDefault(s => s.Id == payment.StudentId);
        var item = new ListViewItem(new string[] { Time.Local(payment.TimeUtc).ToString("dd MMM yyyy HH:mm"), student == null ? "Student" : student.Name,
          payment.Undone ? "Payment (undone)" : "Payment received", payment.Count.ToString(), Money(payment.Amount, currency) }); item.Tag = payment;
        rows.Add(Tuple.Create(payment.Sequence, item));
      }
      foreach (var row in rows.OrderByDescending(r => r.Item1)) list.Items.Add(row.Item2);
      Controls.Add(list);
      undo = Theme.Button("Undo selected payment", 20, 412, 200, 32, false); undo.Enabled = false; Controls.Add(undo);
      list.SelectedIndexChanged += delegate { SelectedPayment = list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as Payment : null; undo.Enabled = ledger.CanUndo(SelectedPayment); };
      undo.Click += delegate { DialogResult = DialogResult.OK; Close(); };
      var close = Theme.Button("Close", 620, 412, 100, 32, true); close.DialogResult = DialogResult.Cancel; Controls.Add(close); CancelButton = close;
    }
    static string Money(decimal amount, string currency) { return amount.ToString("0.00", CultureInfo.InvariantCulture) + " " + currency; }
  }
  public class UpcomingLessonsDialog : Form {
    readonly Ledger ledger;
    readonly Func<CalendarCache> getCache;
    readonly Func<bool> isSyncing;
    readonly ListView list;
    readonly Label details, empty;
    readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
    string lastRows = "";
    public UpcomingLessonsDialog(Ledger current, Func<CalendarCache> calendar, Func<bool> syncing) {
      ledger = current; getCache = calendar; isSyncing = syncing;
      Theme.Dialog(this, "Upcoming lessons", 880, 540);
      Controls.Add(Theme.Label("UPCOMING LESSONS", 20, 18, 840, 28, 11, Theme.Accent, FontStyle.Bold));
      details = Theme.Label("", 20, 55, 840, 42, 9, Theme.Muted, FontStyle.Regular); Controls.Add(details);
      list = new ListView { Location = new Point(20, 104), Size = new Size(840, 340), View = View.Details,
        FullRowSelect = true, MultiSelect = false, BackColor = Theme.Card, ForeColor = Theme.Light,
        BorderStyle = BorderStyle.None, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable };
      list.Columns.Add("Date", 150); list.Columns.Add("Time", 70); list.Columns.Add("Student", 145);
      list.Columns.Add("Expected pay", 125); list.Columns.Add("Calendar event", 330); Controls.Add(list);
      empty = Theme.Label("", 34, 160, 800, 60, 10, Theme.Muted, FontStyle.Regular); empty.BackColor = Theme.Card; Controls.Add(empty); empty.BringToFront();
      Controls.Add(Theme.Label("Expected pay uses current student prices; viewing this list does not change balances.\nOnly tagged events from the latest calendar sync are shown, up to 35 days ahead.", 20, 460, 725, 56, 9, Theme.Muted, FontStyle.Regular));
      var close = Theme.Button("Close", 760, 492, 100, 32, true); close.DialogResult = DialogResult.Cancel;
      Controls.Add(close); CancelButton = close; AcceptButton = close;
      refresh.Interval = 1000; refresh.Tick += delegate { RefreshLessons(); };
      Shown += delegate { RefreshLessons(); refresh.Start(); };
      RefreshLessons();
    }
    void RefreshLessons() {
      var cache = getCache(); var rows = UpcomingLessons.Build(ledger, cache, DateTime.UtcNow);
      bool hasSync = cache != null && !String.IsNullOrEmpty(cache.SyncedUtc);
      string lastSync = "No successful calendar sync yet";
      if (hasSync) {
        try { lastSync = "Last successful sync: " + Time.Local(cache.SyncedUtc).ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture); }
        catch { lastSync = "Last sync time unavailable"; }
      }
      string information = rows.Count + (rows.Count == 1 ? " upcoming lesson" : " upcoming lessons") + "  |  Times: Europe/Vilnius\n" + lastSync + (isSyncing() ? "  |  Sync in progress..." : "");
      if (details.Text != information) details.Text = information;
      string emptyText = hasSync ? "No upcoming lessons in the latest calendar sync.\nClose this list and click Sync to check for calendar updates." : "No calendar lessons downloaded yet.\nConnect your calendar in Settings, then click Sync.";
      if (empty.Text != emptyText) empty.Text = emptyText;
      empty.Visible = rows.Count == 0;
      string signature = Json.Write(new { Rows = rows, Currency = ledger.Data.Settings.Currency });
      if (signature == lastRows) return;
      list.BeginUpdate();
      try {
        list.Items.Clear();
        foreach (var row in rows) {
          DateTime local = TimeZoneInfo.ConvertTimeFromUtc(row.StartUtc, Time.Zone);
          string amount = row.ExpectedPay.HasValue ? row.ExpectedPay.Value.ToString("0.00", CultureInfo.InvariantCulture) + " " + ledger.Data.Settings.Currency : row.PriceNote;
          var item = new ListViewItem(new string[] { local.ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture), local.ToString("HH:mm", CultureInfo.InvariantCulture), row.StudentName, amount, row.CalendarTitle });
          if (!row.ExpectedPay.HasValue) item.ForeColor = Theme.Amber;
          list.Items.Add(item);
        }
        lastRows = signature;
      } finally { list.EndUpdate(); }
    }
    protected override void Dispose(bool disposing) { if (disposing) refresh.Dispose(); base.Dispose(disposing); }
  }
  public class Widget : Form {
    readonly Store store; Ledger ledger; CalendarCache cache; readonly bool preview; bool inspection; readonly string root;
    FlowLayoutPanel students; Panel question; Label total, syncLabel, pendingLabel; Button addStudent, history, upcoming, sync; NotifyIcon tray;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer settingsTimer = new System.Windows.Forms.Timer();
    bool syncing, modal, shuttingDown, readyLogged; IntPtr desktopParent, lastForeground; DateTime lastAttempt = DateTime.MinValue; string syncStatus = "Connect your Google Calendar";
    string lastStudents = "", lastQuestion = ""; int pendingIndex, studentRebuilds, questionRebuilds, completedSyncs; readonly ToolTip tips = new ToolTip();
    public Widget(string dataFolder, bool isPreview, bool openSettings, bool inspectDesktop) {
      preview = isPreview; inspection = inspectDesktop; root = AppDomain.CurrentDomain.BaseDirectory; store = new Store(dataFolder); ledger = new Ledger(store.Load()); cache = store.LoadCache();
      if (cache == null || cache.Events == null) cache = new CalendarCache();
      Text = "Lesson Ledger"; BackColor = Theme.Background; ForeColor = Theme.Light; Font = Theme.Font(10, FontStyle.Regular);
      FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = preview || inspection; StartPosition = FormStartPosition.Manual;
      ClientSize = new Size(400, 580); AutoScaleMode = AutoScaleMode.Dpi; DoubleBuffered = true;
      if (preview) Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 40, Screen.PrimaryScreen.WorkingArea.Top + 40);
      Controls.Add(Theme.Label("LESSON LEDGER", 22, 19, 260, 25, 10, Theme.Accent, FontStyle.Bold));
      var settings = Theme.Button("Settings", 302, 15, 78, 28, false); settings.Click += delegate { OpenSettings(); }; Controls.Add(settings);
      Controls.Add(Theme.Label("Unpaid lessons", 22, 56, 350, 25, 16, Theme.Light, FontStyle.Bold));
      total = Theme.Label("0.00 EUR", 22, 91, 350, 43, 26, Theme.Light, FontStyle.Bold); Controls.Add(total);
      Controls.Add(Theme.Label("TOTAL OWED", 24, 137, 350, 20, 8, Theme.Muted, FontStyle.Bold));
      students = new FlowLayoutPanel { Location = new Point(20, 172), Size = new Size(360, 195), AutoScroll = false, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Background }; Controls.Add(students);
      addStudent = Theme.Button("+ Student", 22, 381, 98, 30, false); addStudent.Click += delegate { EditStudent(null, null); }; Controls.Add(addStudent);
      history = Theme.Button("History", 128, 381, 68, 30, false); history.Click += delegate { OpenHistory(); }; Controls.Add(history);
      upcoming = Theme.Button("Upcoming", 204, 381, 100, 30, false); upcoming.Click += delegate { OpenUpcoming(); }; Controls.Add(upcoming);
      tips.SetToolTip(upcoming, "View upcoming calendar lessons and their expected pay.");
      sync = Theme.Button("Sync", 312, 381, 66, 30, false); sync.Click += delegate { StartSync(true); }; Controls.Add(sync);
      question = new Panel { Location = new Point(20, 426), Size = new Size(360, 108), BackColor = Theme.Card, Visible = false }; Controls.Add(question);
      syncLabel = Theme.Label(syncStatus, 22, 548, 350, 21, 8, Theme.Muted, FontStyle.Regular); Controls.Add(syncLabel);
      tips.SetToolTip(syncLabel, "Google Calendar refreshes every 5 minutes. Questions appear at the scheduled start time.");
      var menu = new ContextMenuStrip(); menu.Items.Add("Settings", null, delegate { OpenSettings(); }); menu.Items.Add("Sync now", null, delegate { StartSync(true); });
      menu.Items.Add("Reattach to desktop", null, delegate { desktopParent = IntPtr.Zero; AttachDesktop(); }); menu.Items.Add(new ToolStripSeparator());
      menu.Items.Add("Quit", null, delegate { shuttingDown = true; Close(); }); ContextMenuStrip = menu;
      tray = new NotifyIcon { Text = "Lesson Ledger", Icon = SystemIcons.Information, Visible = !preview, ContextMenuStrip = menu };
      tray.DoubleClick += delegate { OpenSettings(); };
      FormClosing += delegate { timer.Stop(); settingsTimer.Stop(); tray.Visible = false; tray.Dispose(); };
      timer.Interval = 15000; timer.Tick += delegate { if (!modal) { AttachDesktop(); Render(false); if (DateTime.UtcNow - lastAttempt > TimeSpan.FromMinutes(5)) StartSync(false); } };
      settingsTimer.Interval = 500; settingsTimer.Tick += delegate {
        if (!modal && inspection && Program.InspectionFinished.WaitOne(0)) {
          inspection = false;
          // Preserve the live HWND so the same real instance can be captured after
          // inspection. Changing Form.ShowInTaskbar would recreate its window.
          Native.ShowWindow(Handle, 0);
          int styles = Native.GetWindowLong(Handle, -20);
          Native.SetWindowLong(Handle, -20, (styles & ~0x00040000) | 0x80 | 0x08000000);
          Native.ShowWindow(Handle, 4); desktopParent = IntPtr.Zero; AttachDesktop(); Render(true);
        }
        if (!modal && Program.SettingsRequest.WaitOne(0)) OpenSettings();
        if (!modal && !preview) MaintainDesktopOrder();
      };
      Shown += delegate { AttachDesktop(); Render(true); timer.Start(); settingsTimer.Start(); if (!preview) StartSync(false); if (openSettings) BeginInvoke(new Action(OpenSettings)); };
      Paint += delegate(object sender, PaintEventArgs e) { using (var pen = new Pen(Color.FromArgb(53, 63, 79))) e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1); };
    }
    protected override bool ShowWithoutActivation { get { return !preview; } }
    protected override CreateParams CreateParams {
      get { var cp = base.CreateParams; if (!preview && !inspection) cp.ExStyle |= 0x80 | 0x08000000; return cp; }
    }
    protected override void WndProc(ref Message message) {
      // Show desktop must not minimize the widget along with ordinary applications.
      if (!preview && message.Msg == 0x0112 && (message.WParam.ToInt64() & 0xFFF0) == 0xF020) { message.Result = IntPtr.Zero; return; }
      base.WndProc(ref message);
    }
    void MaintainDesktopOrder() {
      IntPtr foreground = Native.GetForegroundWindow();
      if (foreground != lastForeground || Native.IsIconic(Handle) || !Native.IsWindow(desktopParent)) AttachDesktop();
    }
    void AttachDesktop() {
      if (preview || modal || IsDisposed) return;
      IntPtr host = Native.DesktopHost();
      if (host == IntPtr.Zero) { syncLabel.Text = "Waiting for the Windows desktop..."; return; }
      // Keep the same unowned top-level window used by the working preview. Neither
      // child parenting nor shell ownership is required to manage the desktop order.
      if (Native.GetWindow(Handle, 4) != IntPtr.Zero) Native.SetOwner(Handle, IntPtr.Zero);
      desktopParent = host;
      var screens = Screen.AllScreens; int index = ledger.Data.Settings.Monitor < 0 ? Array.FindIndex(screens, s => s.Primary) : Math.Max(0, Math.Min(ledger.Data.Settings.Monitor, screens.Length - 1));
      var area = screens[index].WorkingArea;
      lastForeground = Native.GetForegroundWindow();
      string foregroundClass = Native.ClassName(lastForeground);
      bool desktopShowing = lastForeground == host || foregroundClass == "Progman" || foregroundClass == "WorkerW" || foregroundClass == "SHELLDLL_DefView";
      if (Native.IsIconic(Handle) || !Native.IsWindowVisible(Handle)) Native.ShowWindow(Handle, 4);
      // Insert immediately above the wallpaper's window and below application windows.
      // Show desktop can raise the wallpaper, so foreground changes also restore order.
      IntPtr insertAfter = desktopShowing ? IntPtr.Zero : Native.WindowAboveDesktop(host, Handle);
      if (insertAfter != IntPtr.Zero && (Native.GetWindowLong(insertAfter, -20) & 8) != 0) insertAfter = new IntPtr(-2);
      Native.SetWindowPos(Handle, insertAfter, area.Right - Width - 22, area.Top + 22, 0, 0,
        0x0010 | 0x0001 | 0x0040 | 0x0200);
      WriteDiagnostics();
    }
    void WriteDiagnostics() {
      try {
        if (!readyLogged) { readyLogged = true; Program.LogLaunch("desktop-ready"); }
        Native.RECT rectangle; Native.GetWindowRect(Handle, out rectangle);
        File.WriteAllText(Path.Combine(store.Folder, "desktop-status.json"), Json.Write(new {
          DesktopHost = Native.ClassName(desktopParent), DesktopOwner = Native.ClassName(Native.GetWindow(Handle, 4)), ChildWindow = (Native.GetWindowLong(Handle, -16) & 0x40000000) != 0,
          Visible = Native.IsWindowVisible(Handle), Topmost = (Native.GetWindowLong(Handle, -20) & 8) != 0, Inspection = inspection,
          Foreground = Native.ClassName(lastForeground), WindowHandle = Handle.ToInt64(),
          WindowAbove = Native.ClassName(Native.VisibleNeighbor(Handle, 3)), WindowBelow = Native.ClassName(Native.VisibleNeighbor(Handle, 2)),
          Left = rectangle.Left, Top = rectangle.Top, Width = rectangle.Right - rectangle.Left, Height = rectangle.Bottom - rectangle.Top,
          AutoStart = Startup.Enabled(), StartupTask = Startup.TaskName, ProcessId = Process.GetCurrentProcess().Id, UpdatedUtc = Time.Now(),
          StudentCount = students.Controls.Count, StudentListHeight = students.Height, StudentScroll = students.AutoScroll,
          AllStudentsVisible = students.Controls.Cast<Control>().All(c => c.Top >= 0 && c.Bottom <= students.ClientSize.Height), QuestionVisible = question.Visible,
          StudentRebuilds = studentRebuilds, QuestionRebuilds = questionRebuilds, Syncing = syncing, CompletedSyncs = completedSyncs,
          Screens = Screen.AllScreens.Select(s => new { Primary = s.Primary, Bounds = s.Bounds.ToString(), WorkingArea = s.WorkingArea.ToString() }).ToArray() }));
      } catch { }
    }
    string Money(decimal amount) { return amount.ToString("0.00", CultureInfo.InvariantCulture) + " " + ledger.Data.Settings.Currency; }
    List<ScheduledLesson> Pending() {
      DateTime from = Time.TrackingStart(ledger.Data.Settings);
      return cache.Events.Where(e => Time.Utc(e.StartUtc) >= from && Time.Utc(e.StartUtc) <= DateTime.UtcNow && !ledger.Answered(e.Key))
        .OrderBy(e => e.StartUtc).ThenBy(e => e.Key).ToList();
    }
    void Mutate(Action change) {
      string snapshot = Json.Write(ledger.Data);
      try { change(); store.Save(ledger.Data); Render(true); }
      catch (Exception exception) { ledger.Data = Json.Read<State>(snapshot); ShowError(exception.Message); Render(true); }
    }
    void Render(bool force) {
      // A sync status change must not replace controls or relayout the desktop window.
      string status = syncing ? "Syncing Google Calendar..." : syncStatus;
      if (syncLabel.Text != status) { syncLabel.Text = status; tips.SetToolTip(syncLabel, status); }
      var pending = Pending();
      var active = ledger.Data.Students.Where(s => s.Active).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
      pendingIndex = pending.Count == 0 ? 0 : Math.Max(0, Math.Min(pendingIndex, pending.Count - 1));
      var selected = pending.Count == 0 ? null : pending[pendingIndex];
      var selectedStudent = selected == null ? null : ledger.Match(selected.StudentName);
      string studentSignature = Json.Write(new { Students = active.Select(s => new { Student = s, Count = ledger.Count(s), Balance = ledger.Balance(s) }).ToArray(), Currency = ledger.Data.Settings.Currency });
      string questionSignature = Json.Write(new { Count = pending.Count, Index = pendingIndex, Lesson = selected,
        StudentId = selectedStudent == null ? null : selectedStudent.Id, Price = selectedStudent == null ? 0 : selectedStudent.Price, Currency = ledger.Data.Settings.Currency });
      bool updateStudents = force || studentSignature != lastStudents;
      bool updateQuestion = force || questionSignature != lastQuestion;
      if (!updateStudents && !updateQuestion) { if (!preview) WriteDiagnostics(); return; }
      SuspendLayout();
      if (updateStudents) {
        students.SuspendLayout();
        foreach (Control child in students.Controls.Cast<Control>().ToArray()) child.Dispose(); students.Controls.Clear();
        total.Text = Money(active.Sum(s => ledger.Balance(s)));
        if (active.Count == 0) {
          var empty = Theme.Label("Add your students and opening balances.\nThen connect your calendar in Settings.", 0, 0, 335, 70, 10, Theme.Muted, FontStyle.Regular); students.Controls.Add(empty);
        }
        foreach (var student in active) {
          var panel = new Panel { Size = new Size(360, 76), BackColor = Theme.Card, Margin = new Padding(0, 0, 0, 8) };
          panel.Controls.Add(Theme.Label(student.Name, 13, 9, 191, 25, 12, Theme.Light, FontStyle.Bold));
          int lessonCount = ledger.Count(student);
          panel.Controls.Add(Theme.Label(lessonCount + (lessonCount == 1 ? " unpaid lesson" : " unpaid lessons"), 13, 40, 189, 23, 9, Theme.Muted, FontStyle.Regular));
          var balance = Theme.Label(Money(ledger.Balance(student)), 203, 9, 144, 25, 12, Theme.Accent, FontStyle.Bold); balance.TextAlign = ContentAlignment.TopRight; panel.Controls.Add(balance);
          var edit = Theme.Button("Edit", 211, 39, 61, 26, false); edit.Click += delegate { EditStudent(student, null); }; panel.Controls.Add(edit);
          var paid = Theme.Button("Paid", 281, 39, 65, 26, true); paid.Enabled = ledger.Count(student) != 0 || ledger.Balance(student) != 0; paid.Click += delegate { Mutate(delegate { ledger.Pay(student); }); }; panel.Controls.Add(paid);
          tips.SetToolTip(edit, student.Price > 0 ? "Lesson price: " + Money(student.Price) + ". Edit price or unpaid balance." : "Set this student's lesson price and opening balance.");
          tips.SetToolTip(paid, "Mark the full current balance as paid. Undo is available in History."); students.Controls.Add(panel);
        }
        students.Height = students.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical);
        if (students.Controls.Count > 0) students.Height -= students.Controls[students.Controls.Count - 1].Margin.Bottom;
        students.ResumeLayout(true);
        lastStudents = studentSignature; studentRebuilds++;
      }
      if (updateQuestion) {
        question.SuspendLayout();
        foreach (Control child in question.Controls.Cast<Control>().ToArray()) child.Dispose(); question.Controls.Clear();
        if (pending.Count > 0) {
          var lesson = selected; var student = selectedStudent;
          pendingLabel = Theme.Label((pendingIndex + 1) + " / " + pending.Count + "  TO CONFIRM", 14, 9, 240, 18, 8, Theme.Amber, FontStyle.Bold); question.Controls.Add(pendingLabel);
          if (pending.Count > 1) { var next = Theme.Button("Next", 278, 6, 68, 24, false); next.Click += delegate { pendingIndex = (pendingIndex + 1) % pending.Count; Render(false); }; question.Controls.Add(next); }
          question.Controls.Add(Theme.Label(lesson.StudentName, 14, 31, 332, 23, 11, Theme.Light, FontStyle.Bold));
          question.Controls.Add(Theme.Label(Time.Local(lesson.StartUtc).ToString("ddd, dd MMM  HH:mm") + " - Did it happen?", 14, 56, 332, 19, 8.5f, Theme.Muted, FontStyle.Regular));
          var no = Theme.Button("No", 256, 78, 90, 25, false); no.Click += delegate {
            Mutate(delegate {
              var matched = ledger.Match(lesson.StudentName);
              if (matched == null) { matched = new Student { Name = lesson.StudentName, CalendarName = lesson.StudentName }; ledger.Data.Students.Add(matched); }
              ledger.Answer(lesson, matched, false); pendingIndex = 0;
            });
          }; question.Controls.Add(no);
          if (student == null || student.Price <= 0) {
            var setup = Theme.Button(student == null ? "Add student / price" : "Set lesson price", 14, 78, 228, 25, true);
            setup.Click += delegate { EditStudent(student, lesson.StudentName); }; question.Controls.Add(setup);
          } else {
            var yes = Theme.Button("Yes  + " + Money(student.Price), 14, 78, 228, 25, true);
            yes.Click += delegate { Mutate(delegate { ledger.Answer(lesson, ledger.Match(lesson.StudentName), true); pendingIndex = 0; }); }; question.Controls.Add(yes);
          }
        }
        question.ResumeLayout(true);
        lastQuestion = questionSignature; questionRebuilds++;
      }
      // Size the list for every complete row, then place the footer below it.
      // The question card reserves space only while a confirmation is due.
      int actionsTop = students.Bottom + 14;
      addStudent.Top = history.Top = upcoming.Top = sync.Top = actionsTop;
      question.Top = sync.Bottom + 15; question.Visible = pending.Count > 0;
      syncLabel.Top = (pending.Count > 0 ? question.Bottom : sync.Bottom) + 14;
      int height = syncLabel.Bottom + 11;
      bool resized = ClientSize.Height != height;
      if (resized) ClientSize = new Size(ClientSize.Width, height);
      ResumeLayout(true); Invalidate();
      if (!preview) { if (resized) AttachDesktop(); else WriteDiagnostics(); }
    }
    void WithDialog(Action action) {
      modal = true;
      try { action(); } catch (Exception exception) { ShowError(exception.Message); }
      finally { modal = false; AttachDesktop(); }
    }
    void EditStudent(Student student, string suggestedName) {
      WithDialog(delegate {
        Student edit = student;
        if (student == null && !String.IsNullOrEmpty(suggestedName)) edit = new Student { Name = suggestedName, CalendarName = suggestedName };
        using (var dialog = new StudentDialog(edit, student == null ? 0 : ledger.Count(student), student == null ? 0 : ledger.Balance(student), ledger.Data.Settings.Currency)) {
          if (dialog.ShowDialog() != DialogResult.OK) return;
          if (ledger.Data.Students.Any(s => s.Id != (student == null ? "" : student.Id) && Ledger.NameKey(s.CalendarName) == Ledger.NameKey(dialog.CalendarName))) { ShowError("Another student already has that calendar name."); return; }
          Mutate(delegate {
            var target = student ?? new Student(); if (student == null) ledger.Data.Students.Add(target);
            target.Name = dialog.StudentName; target.CalendarName = dialog.CalendarName; target.Price = dialog.Price; target.Active = true;
            ledger.SetTotals(target, dialog.LessonCount, dialog.Amount); ++ledger.Data.Sequence;
          });
        }
      });
    }
    void OpenSettings() {
      WithDialog(delegate {
        using (var dialog = new SettingsDialog(ledger.Data.Settings, store.GetFeed())) {
          if (dialog.ShowDialog() != DialogResult.OK) return;
          string previousFeed = store.GetFeed(); var previousState = Json.Write(ledger.Data); bool previousStartup = Startup.Enabled();
          try {
            if (dialog.Feed != previousFeed) store.SetFeed(dialog.Feed);
            if (dialog.AutoStart != previousStartup) Startup.Set(dialog.AutoStart);
            bool resetCache = dialog.Marker != ledger.Data.Settings.Marker || dialog.Feed != previousFeed;
            ledger.Data.Settings.Marker = dialog.Marker; ledger.Data.Settings.Currency = dialog.Currency;
            ledger.Data.Settings.TrackingDate = dialog.TrackingDate; ledger.Data.Settings.Monitor = dialog.MonitorIndex;
            ++ledger.Data.Sequence; store.Save(ledger.Data);
            if (resetCache) { cache = new CalendarCache(); Store.AtomicWrite(Path.Combine(store.Folder, "calendar.json"), Json.Write(cache), false); }
            lastAttempt = DateTime.MinValue; syncStatus = dialog.Feed.Length == 0 ? "Connect your Google Calendar" : "Ready to sync";
            Render(true);
          } catch {
            ledger.Data = Json.Read<State>(previousState);
            try { store.SetFeed(previousFeed); Startup.Set(previousStartup); } catch { }
            throw;
          }
        }
      });
      StartSync(true);
    }
    void OpenHistory() {
      WithDialog(delegate { using (var history = new HistoryDialog(ledger, ledger.Data.Settings.Currency)) {
        if (history.ShowDialog() == DialogResult.OK && history.SelectedPayment != null) Mutate(delegate { ledger.Undo(history.SelectedPayment); });
      } });
    }
    void OpenUpcoming() {
      WithDialog(delegate { using (var dialog = new UpcomingLessonsDialog(ledger, delegate { return cache; }, delegate { return syncing; })) dialog.ShowDialog(); });
    }
    void ShowError(string message) { MessageBox.Show(message, "Lesson Ledger", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    void StartSync(bool manual) {
      if (preview || syncing || shuttingDown) return;
      string feed;
      try { feed = store.GetFeed(); } catch { syncStatus = "Reconnect your calendar in Settings"; Render(false); return; }
      lastAttempt = DateTime.UtcNow;
      if (feed.Length == 0) { syncStatus = "Connect your Google Calendar in Settings"; Render(false); return; }
      var configuration = Json.Write(new { url = feed, marker = ledger.Data.Settings.Marker, fromEpoch = Time.Epoch(Time.TrackingStart(ledger.Data.Settings)),
        toEpoch = Time.Epoch(DateTime.UtcNow.AddDays(35)), timezone = "Europe/Vilnius" });
      var generation = Json.Write(ledger.Data.Settings) + "|" + feed;
      syncing = true; Render(false);
      var worker = new BackgroundWorker();
      worker.DoWork += delegate(object sender, DoWorkEventArgs args) {
        var info = new ProcessStartInfo(Path.Combine(root, "vendor", "node.exe"), "\"" + Path.Combine(root, "calendar.mjs") + "\"") {
          UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true,
          RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root,
          StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        using (var process = Process.Start(info)) {
          var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
          process.StandardInput.Write(configuration); process.StandardInput.Close();
          if (!process.WaitForExit(45000)) { process.Kill(); throw new Exception("Calendar sync timed out. Existing questions are saved."); }
          string errorText = error.Result;
          if (process.ExitCode != 0) throw new Exception(errorText.Length == 0 ? "Calendar sync failed. Check your connection." : errorText);
          var result = Json.Read<CalendarCache>(output.Result);
          if (result == null || result.Events == null || result.Events.Any(e => String.IsNullOrEmpty(e.Key) || String.IsNullOrWhiteSpace(e.StudentName) || String.IsNullOrEmpty(e.StartUtc))) throw new Exception("Calendar returned incomplete data.");
          foreach (var scheduled in result.Events) Time.Utc(scheduled.StartUtc);
          args.Result = result;
        }
      };
      worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs args) {
        syncing = false; completedSyncs++;
        try {
          if (IsDisposed || shuttingDown) return;
          if (generation != Json.Write(ledger.Data.Settings) + "|" + store.GetFeed()) { lastAttempt = DateTime.MinValue; return; }
          if (args.Error != null) { syncStatus = "Offline - saved questions kept. Click Sync to retry."; tips.SetToolTip(syncLabel, args.Error.Message); }
          else {
            var fresh = (CalendarCache)args.Result;
            Store.AtomicWrite(Path.Combine(store.Folder, "calendar.json"), Json.Write(fresh), false); cache = fresh;
            syncStatus = "Synced " + Time.Local(fresh.SyncedUtc).ToString("HH:mm") + "  /  " + fresh.Events.Count + " tagged lessons";
          }
        } catch (Exception exception) { syncStatus = "Could not save calendar. Click Sync to retry."; if (manual) ShowError(exception.Message); }
        finally { if (!IsDisposed && !shuttingDown) Render(false); worker.Dispose(); }
      };
      worker.RunWorkerAsync();
    }
  }
  public static class Program {
    public static EventWaitHandle SettingsRequest;
    public static EventWaitHandle InspectionFinished;
    public static void LogLaunch(string message) {
      try {
        string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "launch.log");
        if (File.Exists(path) && new FileInfo(path).Length > 65536) { string[] lines = File.ReadAllLines(path); File.WriteAllLines(path, lines.Skip(Math.Max(0, lines.Length - 200))); }
        File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " pid=" + Process.GetCurrentProcess().Id + " session=" + Process.GetCurrentProcess().SessionId + " " + message + Environment.NewLine);
      } catch { }
    }
    [STAThread] public static int Main(string[] args) {
      string root = AppDomain.CurrentDomain.BaseDirectory;
      try {
        if (args.Contains("--self-test")) { string result = SelfTest.Run(); File.WriteAllText(Path.Combine(root, "test-results.txt"), result); return 0; }
        if (args.Contains("--enable-startup")) { Startup.Set(true); return 0; }
        if (args.Contains("--disable-startup")) { Startup.Set(false); return 0; }
        if (args.Contains("--finish-inspection")) {
          using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LessonLedger-InspectionFinished")) signal.Set();
          return 0;
        }
        bool preview = args.Contains("--preview") || Process.GetCurrentProcess().ProcessName.EndsWith("Preview", StringComparison.OrdinalIgnoreCase);
        if (!preview) LogLaunch(args.Contains("--startup") ? "startup-launch" : "manual-launch");
        string data = preview ? Path.Combine(root, "qa", "preview") : Path.Combine(root, "Data");
        int dataArg = Array.IndexOf(args, "--data"); if (dataArg >= 0 && dataArg + 1 < args.Length) data = Path.GetFullPath(args[dataArg + 1]);
        bool created;
        using (SettingsRequest = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LessonLedger-Settings-" + (preview ? "Preview" : "Desktop")))
        using (InspectionFinished = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LessonLedger-InspectionFinished"))
        using (var mutex = new Mutex(true, "Local\\LessonLedger-" + (preview ? "Preview" : "Desktop"), out created)) {
          if (!created) { if (args.Contains("--startup")) LogLaunch("already-running"); else SettingsRequest.Set(); return 0; }
          Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
          Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { MessageBox.Show("The action could not be completed. Your saved records have been kept.\n\n" + e.Exception.Message, "Lesson Ledger"); };
          Application.Run(new Widget(data, preview, args.Contains("--settings"), args.Contains("--inspect-desktop")));
        }
        if (!preview) LogLaunch("closed");
        return 0;
      } catch (Exception exception) {
        LogLaunch("failed type=" + exception.GetType().Name + " code=" + exception.HResult.ToString("X8"));
        File.WriteAllText(Path.Combine(root, "last-error.txt"), exception.ToString());
        if (!args.Contains("--self-test") && !args.Contains("--startup") && !args.Contains("--enable-startup") && !args.Contains("--disable-startup")) MessageBox.Show(exception.Message, "Lesson Ledger", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 1;
      }
    }
  }
  public static class SelfTest {
    static int assertions;
    static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); assertions++; }
    public static string Run() {
      assertions = 0; var state = new State(); var ledger = new Ledger(state); var student = new Student { Name = "Alex", CalendarName = "ALEX", Price = 20 }; state.Students.Add(student);
      ledger.SetTotals(student, 2, 35); Check(ledger.Count(student) == 2 && ledger.Balance(student) == 35, "opening totals");
      var one = new ScheduledLesson { Key = "one", StudentName = "ALEX", StartUtc = "2026-10-02T12:00:00Z", Title = "[lesson] ALEX" };
      ledger.Answer(one, student, true); ledger.Answer(one, student, true); Check(ledger.Count(student) == 3 && ledger.Balance(student) == 55, "idempotent confirmation");
      student.Price = 25; Check(ledger.Balance(student) == 55, "historical rate unchanged");
      var two = new ScheduledLesson { Key = "two", StudentName = "ALEX", StartUtc = "2026-10-03T12:00:00Z", Title = "[lesson] ALEX" }; ledger.Answer(two, student, true);
      Check(ledger.Count(student) == 4 && ledger.Balance(student) == 80, "new rate applied");
      var three = new ScheduledLesson { Key = "three", StudentName = "ALEX", StartUtc = "2026-10-04T12:00:00Z", Title = "[lesson] ALEX" }; ledger.Answer(three, student, false);
      Check(ledger.Answered("three") && ledger.Count(student) == 4 && ledger.Balance(student) == 80, "No remembered without charge");
      var payment = ledger.Pay(student); Check(payment.Count == 4 && payment.Amount == 80 && ledger.Count(student) == 0 && ledger.Balance(student) == 0, "payment clears both totals");
      Check(ledger.Answered("one"), "paid lessons cannot reappear"); ledger.Undo(payment); Check(ledger.Count(student) == 4 && ledger.Balance(student) == 80, "undo payment restores exact totals");
      ledger.SetTotals(student, 1, 17); Check(ledger.Count(student) == 1 && ledger.Balance(student) == 17, "edit totals corrects absolute balance");
      payment = ledger.Pay(student); ledger.SetTotals(student, 2, 30); Check(!ledger.CanUndo(payment), "unsafe old payment undo blocked");
      Check(ledger.Match(" alex ") == student, "calendar name matching");
      var restored = new Ledger(Json.Read<State>(Json.Write(state))); Check(restored.Count(restored.Data.Students[0]) == 2 && restored.Balance(restored.Data.Students[0]) == 30 && restored.Answered("two"), "JSON restart persistence");
      Check(Time.TrackingStart(state.Settings) == new DateTime(2026, 10, 1, 21, 0, 0, DateTimeKind.Utc), "tracking midnight in Vilnius");
      string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qa", "store-test"); var store = new Store(folder); store.Save(state); store.Save(state);
      Check(File.Exists(store.FilePath + ".bak") && store.Load().Entries.Count == state.Entries.Count, "atomic save and backup");
      const string fakeFeed = "https://calendar.google.com/calendar/ical/example/private-example/basic.ics"; store.SetFeed(fakeFeed);
      Check(store.GetFeed() == fakeFeed && !Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder, "calendar-link.dat"))).Contains(fakeFeed), "private link encrypted at rest");
      int ledgerChecks = assertions;
      var upcomingState = new State(); var upcomingLedger = new Ledger(upcomingState);
      var alex = new Student { Name = "Alex", CalendarName = "ALEX", Price = 22 }; upcomingState.Students.Add(alex);
      upcomingState.Students.Add(new Student { Name = "Casey", CalendarName = "CASEY", Price = 0 });
      upcomingLedger.SetTotals(alex, 1, 8);
      var fixture = new CalendarCache { Events = new List<ScheduledLesson> {
        new ScheduledLesson { Key = "later", StudentName = "ALEX", Title = "[lesson] ALEX", StartUtc = "2026-10-24T13:00:00+03:00" },
        new ScheduledLesson { Key = "past", StudentName = "ALEX", Title = "[lesson] ALEX", StartUtc = "2026-10-24T08:59:59Z" },
        new ScheduledLesson { Key = "next", StudentName = " alex ", Title = "[lesson] Alex", StartUtc = "2026-10-24T09:30:00Z" },
        new ScheduledLesson { Key = "unknown", StudentName = "Morgan", Title = "[lesson] Morgan", StartUtc = "2026-10-24T09:45:00Z" },
        new ScheduledLesson { Key = "no-rate", StudentName = "CASEY", Title = "[lesson] Casey", StartUtc = "2026-10-24T09:50:00Z" }
      } };
      DateTime now = new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
      string original = Json.Write(upcomingState); var scheduledRows = UpcomingLessons.Build(upcomingLedger, fixture, now);
      Check(scheduledRows.Select(r => r.Key).SequenceEqual(new string[] { "next", "unknown", "no-rate", "later" }), "upcoming lessons exclude past events and sort by actual UTC time");
      Check(scheduledRows[0].StudentName == "Alex" && scheduledRows[0].CalendarTitle == "[lesson] Alex", "upcoming lessons match calendar names and retain the original event title");
      Check(scheduledRows[0].ExpectedPay == 22 && upcomingLedger.Balance(alex) == 8, "upcoming pay uses the current price rather than the existing balance");
      Check(!scheduledRows[1].ExpectedPay.HasValue && scheduledRows[1].PriceNote == "Student not added", "unmatched calendar students stay visible without an invented price");
      Check(!scheduledRows[2].ExpectedPay.HasValue && scheduledRows[2].PriceNote == "Price not set", "missing prices are shown explicitly");
      Check(Json.Write(upcomingState) == original, "viewing upcoming lessons does not change saved lesson or payment data");
      Check(UpcomingLessons.Build(upcomingLedger, new CalendarCache(), now).Count == 0, "upcoming view handles an empty calendar cache");
      upcomingState.Settings.TrackingDate = "2026-10-25";
      Check(UpcomingLessons.Build(upcomingLedger, fixture, now).Count == 0, "upcoming view respects the tracking start date");
      return ledgerChecks + " ledger checks and " + (assertions - ledgerChecks) + " upcoming lesson checks passed.\r\n";
    }
  }
}
