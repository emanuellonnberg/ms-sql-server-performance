using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace SqlDiagnostics.UI.Wpf
{
    public class RecommendationLink
    {
        public string Text { get; set; } = string.Empty;
        public string? Url { get; set; }
        public bool IsLink => !string.IsNullOrWhiteSpace(Url);
    }

    public class RecommendationsToLinksConverter : IValueConverter
    {
        private static readonly Regex UrlRegex = new(@"(https?://[\w\-./?%&=]+)", RegexOptions.Compiled);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var lines = value as string;
            if (string.IsNullOrWhiteSpace(lines)) return Array.Empty<RecommendationLink>();
            var result = new List<RecommendationLink>();
            foreach (var line in lines.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var match = UrlRegex.Match(line);
                if (match.Success)
                {
                    result.Add(new RecommendationLink { Text = line, Url = match.Value });
                }
                else
                {
                    result.Add(new RecommendationLink { Text = line });
                }
            }
            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
