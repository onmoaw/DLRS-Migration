using System;
using System.Globalization;
using System.Windows.Data;

namespace LandPressDataMigrationDesktopApp
{
    /// <summary>
    /// Value converter and utility class that transforms English digits ('0'-'9')
    /// into Bengali digits ('০'-'৯') and vice versa.
    /// </summary>
    public class BanglaNumberConverter : IValueConverter
    {
        private static readonly char[] EngDigits = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9'];
        private static readonly char[] BanDigits = ['০', '১', '২', '৩', '৪', '৫', '৬', '৭', '৮', '৯'];

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || value == DBNull.Value) return string.Empty;

            string str = value.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(str)) return string.Empty;

            // Optional decimal formatting via parameter, e.g. parameter="0.0000"
            if (parameter is string fmt && !string.IsNullOrWhiteSpace(fmt))
            {
                if (decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal decVal))
                {
                    str = decVal.ToString(fmt, CultureInfo.InvariantCulture);
                }
            }

            return ToBanglaDigit(str);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            return ToEnglishDigit(value.ToString());
        }

        /// <summary>
        /// Converts any object or string containing English digits ('0'-'9') into Bengali digits ('০'-'৯').
        /// </summary>
        public static string ToBanglaDigit(object? value)
        {
            if (value == null || value == DBNull.Value) return string.Empty;
            string input = value.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(input)) return string.Empty;

            for (int i = 0; i < EngDigits.Length; i++)
            {
                input = input.Replace(EngDigits[i], BanDigits[i]);
            }
            return input;
        }

        /// <summary>
        /// Converts Bengali digits ('০'-'৯') into standard ASCII English digits ('0'-'9').
        /// Used for normalizing search inputs (e.g. typing Bengali digits into Khatian search box).
        /// </summary>
        public static string ToEnglishDigit(string? input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            for (int i = 0; i < BanDigits.Length; i++)
            {
                input = input.Replace(BanDigits[i], EngDigits[i]);
            }
            return input;
        }

        /// <summary>
        /// Formats a decimal number with given precision (default "0.0000") and converts digits to Bengali.
        /// </summary>
        public static string FormatBanglaDecimal(decimal value, string format = "0.0000")
        {
            return ToBanglaDigit(value.ToString(format, CultureInfo.InvariantCulture));
        }
    }
}
