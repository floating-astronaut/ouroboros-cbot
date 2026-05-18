using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    /// <summary>
    /// Port of momentum_hunter.py (viper, M5).
    /// Trigger: RSI(14) crossing 52 up or 48 down within last 5 bars.
    /// Modifier: EMA(20) alignment + volume > 1.3 × 50-bar avg.
    /// Confidence: 0.65 base + 0.10 EMA + 0.15 volume, capped 0.95.
    /// </summary>
    public sealed class MomentumHunter : IStrategy
    {
        public string Id => "momentum_hunter";

        private RelativeStrengthIndex _rsi;
        private ExponentialMovingAverage _ema;
        private AverageTrueRange _atr;

        public void Attach(Robot robot, Bars bars, Cell cell)
        {
            _rsi = robot.Indicators.RelativeStrengthIndex(bars.ClosePrices, 14);
            _ema = robot.Indicators.ExponentialMovingAverage(bars.ClosePrices, 20);
            _atr = robot.Indicators.AverageTrueRange(bars, 14, MovingAverageType.Simple);
        }

        public Signal OnBar(Robot robot, Bars bars, Cell cell)
        {
            if (bars.ClosePrices.Count < 60)
                return Signal.Hold(cell.Bot, cell.Symbol, "insufficient bars");

            double close = bars.ClosePrices.LastValue;
            double curEma = _ema.Result.LastValue;
            double curAtr = _atr.Result.LastValue;

            // RSI crossover within last 5 bars
            int look = 5;
            string cross = "none";
            for (int i = 1; i <= look; i++)
            {
                double prev = _rsi.Result.Last(i);
                double cur  = _rsi.Result.Last(i - 1);
                if (prev <= 52 && cur > 52) { cross = "bull"; break; }
                if (prev >= 48 && cur < 48) { cross = "bear"; break; }
            }
            if (cross == "none")
                return Signal.Hold(cell.Bot, cell.Symbol, "no RSI 48/52 cross in 5 bars");

            // Volume confirmation
            double curVol = bars.TickVolumes.LastValue;
            double avgVol = 0;
            int window = 50;
            int avail = bars.TickVolumes.Count;
            int n = window < avail ? window : avail;
            for (int i = 0; i < n; i++) avgVol += bars.TickVolumes.Last(i);
            avgVol = n > 0 ? avgVol / n : curVol;
            bool volConfirmed = avgVol > 0 && (curVol / avgVol) > 1.3;

            double conf = 0.65;
            if (cross == "bull")
            {
                if (close > curEma) conf += 0.10;
                if (volConfirmed)   conf += 0.15;
                if (conf > 0.95) conf = 0.95;
                return new Signal {
                    Bot = cell.Bot, Symbol = cell.Symbol,
                    Direction = Vote.Buy, Confidence = conf,
                    BarTime = bars.OpenTimes.LastValue, Price = close,
                    Atr = curAtr,
                    Reason = "RSI broke above 52"
                };
            }
            else // bear
            {
                if (close < curEma) conf += 0.10;
                if (volConfirmed)   conf += 0.15;
                if (conf > 0.95) conf = 0.95;
                return new Signal {
                    Bot = cell.Bot, Symbol = cell.Symbol,
                    Direction = Vote.Sell, Confidence = conf,
                    BarTime = bars.OpenTimes.LastValue, Price = close,
                    Atr = curAtr,
                    Reason = "RSI broke below 48"
                };
            }
        }
    }
}
