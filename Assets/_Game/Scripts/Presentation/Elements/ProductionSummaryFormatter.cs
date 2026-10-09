using System;
using System.Collections.Generic;
using Spotlight.Contracts;

namespace Spotlight.Presentation
{
    /// <summary>把 DawnSettledEvent.Rewards 格式化成摘要文本；纯显示，无场景访问与事件发布。</summary>
    public static class ProductionSummaryFormatter
    {
        private const string Prefix = "上夜生产：";
        private const string NoneText = "无";
        private const string OverflowText = "超出显示范围";

        private static readonly string[] DefinitionIds =
            { "elm.water", "elm.fire", "elm.earth", "elm.wood", "elm.wind", "elm.thunder" };
        private static readonly string[] DisplayNames =
            { "水", "火", "土", "木", "风", "雷" };

        public static string Format(IReadOnlyList<ResourceAmount> rewards)
        {
            long[] totals = new long[DefinitionIds.Length];
            bool[] overflowed = new bool[DefinitionIds.Length];
            bool anyValid = false;

            if (rewards != null)
            {
                for (int i = 0; i < rewards.Count; i++)
                {
                    ResourceAmount entry = rewards[i];
                    if (entry.Bucket != ResourceBucket.Hand) continue;
                    if (entry.Amount <= 0) continue;
                    int slot = IndexOfElement(entry.DefinitionId);
                    if (slot < 0) continue;

                    anyValid = true;
                    if (overflowed[slot]) continue;
                    if (long.MaxValue - totals[slot] < entry.Amount) { overflowed[slot] = true; continue; }
                    totals[slot] += entry.Amount;
                }
            }

            if (!anyValid) return Prefix + NoneText;

            List<string> parts = new List<string>();
            for (int i = 0; i < DefinitionIds.Length; i++)
            {
                if (overflowed[i]) parts.Add(DisplayNames[i] + " " + OverflowText);
                else if (totals[i] > 0) parts.Add(DisplayNames[i] + " +" + totals[i].ToString());
            }

            if (parts.Count == 0) return Prefix + NoneText;
            return Prefix + string.Join("、", parts.ToArray());
        }

        private static int IndexOfElement(string definitionId)
        {
            if (string.IsNullOrEmpty(definitionId)) return -1;
            for (int i = 0; i < DefinitionIds.Length; i++)
                if (string.Equals(definitionId, DefinitionIds[i], StringComparison.Ordinal)) return i;
            return -1;
        }
    }
}