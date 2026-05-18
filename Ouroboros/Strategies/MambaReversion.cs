using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    /// <summary>
    /// Port of mamba_reversion.py — Bollinger Band mean reversion in ranging
    /// markets. Used by hydra (M1) and mamba (M15).
    /// Regime: ADX < 25 = ranging → trade, else hold.
    /// Lower BB fade → BUY, upper BB fade → SELL.
    /// Two-tier: AT band requires strict RSI (≤30 / ≥70); NEAR band (15% of width)
    /// requires mild RSI (≤50 / ≥50). SL = band ± ATR; TP = BB mid.
    /// </summary>
    public sealed class MambaReversion : IStrategy
    {
        public string Id => "mamba_reversion";

        private BollingerBands _bb;
        private RelativeStrengthIndex _rsi;
        private DirectionalMovementSystem _adx;   // DMS exposes ADX over arbitrary Bars
        private AverageTrueRange _atr;

        public void Attach(Robot robot, Bars bars, Cell cell)
        {
            _bb  = robot.Indicators.BollingerBands(bars.ClosePrices, 20, 2.0, MovingAverageType.Simple);
            _rsi = robot.Indicators.RelativeStrengthIndex(bars.ClosePrices, 14);
            _adx = robot.Indicators.DirectionalMovementSystem(bars, 14);
            _atr = robot.Indicators.AverageTrueRange(bars, 14, MovingAverageType.Simple);
        }

        public Signal OnBar(Robot robot, Bars bars, Cell cell)
        {
            if (bars.ClosePrices.Count < 60)
                return Signal.Hold(cell.Bot, cell.Symbol, "insufficient bars");

            double upper = _bb.Top.LastValue;
            double mid   = _bb.Main.LastValue;
            double lower = _bb.Bottom.LastValue;
            double adxV  = _adx.ADX.LastValue;
            double atrV  = _atr.Result.LastValue;
            // Use the freshly-closed bar's close
            double price = bars.ClosePrices.Last(0);
            double curRsi = _rsi.Result.Last(0);

            if (atrV <= 0)
                return Signal.Hold(cell.Bot, cell.Symbol, "ATR not ready");
            if (adxV >= 25)
                return Signal.Hold(cell.Bot, cell.Symbol, "trending (ADX>=25)");

            double bbRange = upper - lower;
            if (bbRange <= 0)
                return Signal.Hold(cell.Bot, cell.Symbol, "BB collapsed");
            double proximity = bbRange * 0.15;

            // Lower BB fade → BUY
            if (price <= lower + proximity)
            {
                bool atBand = price <= lower;
                bool rsiOk = atBand ? (curRsi <= 30) : (curRsi < 50);
                if (rsiOk)
                {
                    return new Signal
                    {
                        Bot = cell.Bot, Symbol = cell.Symbol,
                        Direction = Vote.Buy,
                        Confidence = atBand ? 0.70 : 0.60,
                        BarTime = bars.OpenTimes.LastValue, Price = price,
                        Atr = atrV,
                        SuggestedSl = lower - atrV,
                        SuggestedTp = mid,
                        Reason = atBand ? "BB lower fade (at band)" : "BB lower approach"
                    };
                }
            }

            // Upper BB fade → SELL
            if (price >= upper - proximity)
            {
                bool atBand = price >= upper;
                bool rsiOk = atBand ? (curRsi >= 70) : (curRsi > 50);
                if (rsiOk)
                {
                    return new Signal
                    {
                        Bot = cell.Bot, Symbol = cell.Symbol,
                        Direction = Vote.Sell,
                        Confidence = atBand ? 0.70 : 0.60,
                        BarTime = bars.OpenTimes.LastValue, Price = price,
                        Atr = atrV,
                        SuggestedSl = upper + atrV,
                        SuggestedTp = mid,
                        Reason = atBand ? "BB upper fade (at band)" : "BB upper approach"
                    };
                }
            }

            return Signal.Hold(cell.Bot, cell.Symbol, "ranging, mid-band — waiting for extremes");
        }
    }
}
