using System;
using System.Text.RegularExpressions;

namespace HydronicNetworkAuditor.Core
{
    public static class NetworkClassifier
    {
        private static readonly Regex HtToken = new Regex(
            @"(^|[^A-Z0-9])HT([^A-Z0-9]|$)|HIGH\s*[-_ ]?\s*TEMPERATURE",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LtToken = new Regex(
            @"(^|[^A-Z0-9])LT([^A-Z0-9]|$)|LOW\s*[-_ ]?\s*TEMPERATURE",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SupplyToken = new Regex(
            @"SUPPLYHYDRONIC|HYDRONIC\s+SUPPLY|CHWS|(^|[^A-Z0-9])(HTS|LTS)([^A-Z0-9]|$)|(^|[^A-Z0-9])SUPPLY([^A-Z0-9]|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ReturnToken = new Regex(
            @"RETURNHYDRONIC|HYDRONIC\s+RETURN|CHWR|(^|[^A-Z0-9])(HTR|LTR)([^A-Z0-9]|$)|(^|[^A-Z0-9])RETURN([^A-Z0-9]|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static TemperatureNetwork InferTemperatureNetwork(string evidence)
        {
            evidence = evidence ?? string.Empty;

            bool ht = HtToken.IsMatch(evidence);
            bool lt = LtToken.IsMatch(evidence);

            if (ht && lt) return TemperatureNetwork.Mixed;
            if (ht) return TemperatureNetwork.HT;
            if (lt) return TemperatureNetwork.LT;
            return TemperatureNetwork.Unknown;
        }

        public static FlowSide InferFlowSide(string evidence)
        {
            evidence = evidence ?? string.Empty;

            bool supply = SupplyToken.IsMatch(evidence);
            bool ret = ReturnToken.IsMatch(evidence);

            if (supply && ret) return FlowSide.Mixed;
            if (supply) return FlowSide.Supply;
            if (ret) return FlowSide.Return;
            return FlowSide.Unknown;
        }
    }
}
