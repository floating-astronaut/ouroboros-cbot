using System;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    /// <summary>
    /// Port of session_analyst.py (taipan, M30). UTC session bias + EMA(20) slope on M30.
    /// London 07-16, NY 12-21, overlap 12-16 (highest conf), Asian 0-7 (lowest for forex).
    /// EMA rising → BUY, EMA falling → SELL, flat → HOLD.
    /// </summary>
    public sealed class SessionAnalyst : IStrategy
    {
        public string Id => "session_analyst";

        private ExponentialMovingAverage _ema;
        private AverageTrueRange _atr;

        public void Attach(Robot robot, Bars bars, Cell cell)
        {
            _ema = robot.Indicators.ExponentialMovingAverage(bars.ClosePrices, 20);
            _atr = robot.Indicators.AverageTrueRange(bars, 14, MovingAverageType.Simple);
        }

        public Signal OnBar(Robot robot, Bars bars, Cell cell)
        {
            if (bars.ClosePrices.Count < 30)
                return Signal.Hold(cell.Bot, cell.Symbol, "insufficient bars");

            int hour = robot.Time.ToUniversalTime().Hour;
            bool isOverlap = hour >= 12 && hour < 16;
            bool isLondon  = hour >= 7  && hour < 16;
            bool isNy      = hour >= 12 && hour < 21;
            bool isAsian   = hour >= 0  && hour < 7;
            bool isForex   = IsForex(cell.Symbol);

            double baseConf;
            if (isOverlap)            baseConf = 0.9;
            else if (isLondon || isNy) baseConf = 0.8;
            else if (isAsian)          baseConf = isForex ? 0.5 : 0.7;
            else                        baseConf = 0.5;

            // EMA slope: last 5 vs 1 ago
            int avail = _ema.Result.Count;
            if (avail < 6) return Signal.Hold(cell.Bot, cell.Symbol, "EMA warmup");
            double emaNow  = _ema.Result.Last(0);
            double emaPast = _ema.Result.Last(4);
            double slope = emaNow - emaPast;
            double threshold = emaNow * 0.0001;
            string dir = (slope > threshold) ? "up" : (slope < -threshold) ? "down" : "flat";

            if (dir == "flat")
                return Signal.Hold(cell.Bot, cell.Symbol, "EMA flat");

            double conf = baseConf;
            if (isAsian && !isForex) conf *= 0.9; // mild crypto asian penalty
            double close = bars.ClosePrices.LastValue;
            double atrV  = _atr.Result.LastValue;

            return new Signal
            {
                Bot = cell.Bot, Symbol = cell.Symbol,
                Direction = dir == "up" ? Vote.Buy : Vote.Sell,
                Confidence = conf,
                BarTime = bars.OpenTimes.LastValue, Price = close,
                Atr = atrV,
                Reason = string.Format("session={0} ema={1}", SessionTag(hour), dir)
            };
        }

        private static bool IsForex(string sym)
        {
            switch (sym)
            {
                case "EURUSD": case "GBPUSD": case "USDJPY": case "USDCHF":
                case "AUDUSD": case "NZDUSD": case "USDCAD": case "EURJPY":
                case "GBPJPY": case "XAUUSD": case "XAGUSD":
                    return true;
                default: return false;
            }
        }

        private static string SessionTag(int h)
        {
            if (h >= 12 && h < 16) return "overlap";
            if (h >= 7  && h < 16) return "london";
            if (h >= 12 && h < 21) return "ny";
            if (h >= 0  && h < 7)  return "asian";
            return "off";
        }
    }
}
