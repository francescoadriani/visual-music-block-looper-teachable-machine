using System;

namespace VisualMusicBlockLooper.Scene
{
    internal static class CellMatch
    {
        public static string Label(string[] labels, double[] scores, int thresholdPercent)
        {
            if (scores == null || labels.Length == 0 || scores.Length != labels.Length)
                return null;

            int best = 0;
            for (int index = 1; index < scores.Length; index++)
                if (scores[index] > scores[best])
                    best = index;

            if (scores[best] < thresholdPercent / 100.0 ||
                string.Equals(labels[best].Trim(), "VUOTO", StringComparison.OrdinalIgnoreCase))
                return null;

            return labels[best];
        }
    }
}
