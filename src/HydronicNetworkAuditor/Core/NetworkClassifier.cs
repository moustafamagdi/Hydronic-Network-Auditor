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

        public static TemperatureNetwork InferTemperatureNetwork(
            string declaredSystemType,
            string explicitHnaNetwork,
            string fallbackEvidence)
        {
            TemperatureNetwork explicitValue = ParseTemperatureNetwork(explicitHnaNetwork);
            if (explicitValue != TemperatureNetwork.Unknown)
                return explicitValue;

            TemperatureNetwork declared = ParseTemperatureNetwork(declaredSystemType);
            if (declared != TemperatureNetwork.Unknown)
                return declared;

            return ParseTemperatureNetwork(fallbackEvidence);
        }

        public static FlowSide InferFlowSide(
            string declaredSystemClassification,
            string explicitHnaFlowSide,
            string connectorSystemTypes,
            string fallbackEvidence)
        {
            FlowSide explicitValue = ParseFlowSide(explicitHnaFlowSide);
            if (explicitValue != FlowSide.Unknown)
                return explicitValue;

            FlowSide declared = ParseFlowSide(declaredSystemClassification);
            if (declared != FlowSide.Unknown)
                return declared;

            FlowSide connector = InferConnectorFlowSide(connectorSystemTypes);
            if (connector != FlowSide.Unknown)
                return connector;

            return ParseFlowSide(fallbackEvidence);
        }

        public static FlowSide InferConnectorFlowSide(string connectorSystemTypes)
        {
            if (string.IsNullOrWhiteSpace(connectorSystemTypes))
                return FlowSide.Unknown;

            bool supply = connectorSystemTypes.IndexOf("SupplyHydronic", StringComparison.OrdinalIgnoreCase) >= 0;
            bool ret = connectorSystemTypes.IndexOf("ReturnHydronic", StringComparison.OrdinalIgnoreCase) >= 0;

            if (supply && ret) return FlowSide.Mixed;
            if (supply) return FlowSide.Supply;
            if (ret) return FlowSide.Return;
            return FlowSide.Unknown;
        }

        private static TemperatureNetwork ParseTemperatureNetwork(string text)
        {
            text = text ?? string.Empty;
            bool ht = HtToken.IsMatch(text);
            bool lt = LtToken.IsMatch(text);

            if (ht && lt) return TemperatureNetwork.Mixed;
            if (ht) return TemperatureNetwork.HT;
            if (lt) return TemperatureNetwork.LT;
            return TemperatureNetwork.Unknown;
        }

        private static FlowSide ParseFlowSide(string text)
        {
            text = text ?? string.Empty;
            bool supply = SupplyToken.IsMatch(text);
            bool ret = ReturnToken.IsMatch(text);

            if (supply && ret) return FlowSide.Mixed;
            if (supply) return FlowSide.Supply;
            if (ret) return FlowSide.Return;
            return FlowSide.Unknown;
        }
    }
}
