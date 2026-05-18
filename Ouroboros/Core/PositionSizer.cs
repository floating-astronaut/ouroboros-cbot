using System;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    /// <summary>
    /// Converts a Signal + Cell tier + multi-bot agreement count into a volume
    /// (in units, ready for ExecuteMarketOrder) and a stop / target distance in
    /// the symbol's price units.
    ///
    /// Risk model (drawdown-safe by design):
    ///   per_trade_risk_$ = balance * BaseRiskPct * Whitelist.TierMultiplier(cell.Tier) * AgreementMultiplier(n)
    ///   AgreementMultiplier: 0→1.0, 1→1.5, 2→2.0, 3+→2.5 (capped)
    ///   volume_units = per_trade_risk_$ / (sl_distance_in_price * pip_value / pip_size)
    /// </summary>
    public static class PositionSizer
    {
        public struct Plan
        {
            public double VolumeUnits;
            public double StopLossPrice;
            public double TakeProfitPrice;
            public double RiskDollars;
            public int    AgreementCount;
        }

        public static double AgreementMultiplier(int n)
        {
            if (n <= 0) return 1.0;
            if (n == 1) return 1.5;
            if (n == 2) return 2.0;
            return 2.5; // 3+
        }

        public static Plan Build(
            Signal sig, Cell cell, Symbol sym, double accountBalance,
            double baseRiskPct, int agreement, double atrFallbackMult,
            double tpRMultipleNormal, double tpRMultipleTrend)
        {
            double tierMult  = Whitelist.TierMultiplier(cell.Tier);
            double agreeMult = AgreementMultiplier(agreement);
            double riskUsd   = accountBalance * (baseRiskPct / 100.0) * tierMult * agreeMult;

            // SL / TP. Prefer strategy-provided values; fall back to ATR-based.
            double sl, tp;
            if (sig.SuggestedSl.HasValue && sig.SuggestedTp.HasValue && sig.Atr > 0)
            {
                sl = sig.SuggestedSl.Value;
                tp = sig.SuggestedTp.Value;
            }
            else
            {
                double atr = sig.Atr > 0 ? sig.Atr : Math.Max(sym.PipSize * 10, sym.PipSize);
                double slDist = atr * atrFallbackMult;
                double rMult  = (cell.Tier == Tier.Trend) ? tpRMultipleTrend : tpRMultipleNormal;
                double tpDist = slDist * rMult;
                if (sig.Direction == Vote.Buy)
                {
                    sl = sig.Price - slDist;
                    tp = sig.Price + tpDist;
                }
                else
                {
                    sl = sig.Price + slDist;
                    tp = sig.Price - tpDist;
                }
            }

            double slDistAbs = Math.Abs(sig.Price - sl);
            if (slDistAbs <= 0)
            {
                return new Plan { VolumeUnits = 0, StopLossPrice = sl, TakeProfitPrice = tp,
                                  RiskDollars = 0, AgreementCount = agreement };
            }

            // Convert risk_$ → volume in units.
            // For a generic symbol, $ risk per 1 unit per 1 price unit move = sym.PipValue / sym.PipSize.
            // volume_units = riskUsd / (slDistAbs * (PipValue / PipSize))
            double dollarPerUnitPerPrice = sym.PipValue / sym.PipSize;
            if (dollarPerUnitPerPrice <= 0) dollarPerUnitPerPrice = 1.0;
            double rawVolume = riskUsd / (slDistAbs * dollarPerUnitPerPrice);

            // Normalize to broker's allowed step / min / max.
            double volumeUnits = sym.NormalizeVolumeInUnits(rawVolume, RoundingMode.Down);
            if (volumeUnits < sym.VolumeInUnitsMin) volumeUnits = 0; // can't size below broker minimum: skip

            return new Plan
            {
                VolumeUnits     = volumeUnits,
                StopLossPrice   = sl,
                TakeProfitPrice = tp,
                RiskDollars     = riskUsd,
                AgreementCount  = agreement
            };
        }
    }
}
