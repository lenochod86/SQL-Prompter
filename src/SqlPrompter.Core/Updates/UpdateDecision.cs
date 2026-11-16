using System;
using System.Globalization;
using SqlPrompter.Core.Settings;

namespace SqlPrompter.Core.Updates
{
    /// <summary>The rules of the update notification (#297): when to ask GitHub, when to show the window and what the buttons remember.</summary>
    public static class UpdateDecision
    {
        public const string DayFormat = "yyyy-MM-dd";

        public static string Day(DateTime localTime) => localTime.ToString(DayFormat, CultureInfo.InvariantCulture);

        /// <summary>The check is on and has not run today (the last one was on an earlier day, or never, or the stored day is not a date).</summary>
        public static bool ShouldCheck(UpdateSettings settings, DateTime today) =>
            settings.CheckForUpdates && !IsTodayOrLater(settings.LastCheckDay, today);

        /// <summary>True when the window is to be shown: the release is newer than the running version, was not skipped and is not postponed to a later day.</summary>
        public static bool ShouldAnnounce(UpdateSettings settings, Version current, ReleaseInfo release, DateTime today)
        {
            if (!settings.CheckForUpdates || release == null || current == null || release.Version <= current)
            {
                return false;
            }

            if (Version.TryParse(settings.SkippedVersion, out var skipped) && release.Version <= skipped)
            {
                return false;
            }

            // Postponed to a day that has not come yet (a stored text that is not a day does not postpone).
            return !(DateTime.TryParseExact(settings.RemindOn, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var remindOn) && today.Date < remindOn.Date);
        }

        /// <summary>"Remind me tomorrow": the window comes back at the first start on the next calendar day.</summary>
        public static UpdateSettings RemindTomorrow(UpdateSettings settings, DateTime today)
        {
            var copy = settings.Clone();
            copy.RemindOn = Day(today.Date.AddDays(1));
            return copy;
        }

        /// <summary>"Skip this version": this and every older version stay silent, a newer one is announced again.</summary>
        public static UpdateSettings Skip(UpdateSettings settings, ReleaseInfo release)
        {
            var copy = settings.Clone();
            copy.SkippedVersion = release.Version.ToString();
            copy.RemindOn = string.Empty;
            return copy;
        }

        public static UpdateSettings Checked(UpdateSettings settings, DateTime today)
        {
            var copy = settings.Clone();
            copy.LastCheckDay = Day(today);
            return copy;
        }

        private static bool IsTodayOrLater(string day, DateTime today) =>
            DateTime.TryParseExact(day, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var last) && last.Date >= today.Date;
    }
}
