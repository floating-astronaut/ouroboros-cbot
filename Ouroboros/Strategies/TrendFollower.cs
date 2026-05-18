using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    /// <summary>
    /// Port of trend_follower.py (cobra, H1). SMA(9)/EMA(21) cross within last 5 bars
    /// + ADX > 15 trend filter. ATR(14) vs ATR_median(100) is confidence modifier only.
    /// </summary>
    public sealed class TrendFollower : IStrategy
    {
        public string Id => "trend_follower";

        private SimpleMovingAverage _sma;
        private ExponentialMovingAverage _ema;
        private DirectionalMovementSystem _adx;   // DMS exposes ADX over arbitrary Bars
        private AverageTrueRange _atr;

        public void Attach(Robot robot, Bars bars, Cell cell)
        {
            _sma = robot.Indicators.SimpleMovingAverage(bars.ClosePrices, 9);
            _ema = robot.Indicators.ExponentialMovingAverage(bars.ClosePrices, 21);
            _adx = robot.Indicators.DirectionalMovementSystem(bars, 14);
            _atr = robot.Indicators.AverageTrueRange(bars, 14, MovingAverageType.Simple);
        }

        public Signal OnBar(Robot robot, Bars bars, Cell cell)
        {
            if (bars.ClosePrices.Count < 50)
                return Signal.Hold(cell.Bot, cell.Symbol, "insufficient bars");

            double curAdx = _adx.ADX.LastValue;
            double curAtr = _atr.Result.LastValue;

            // Median ATR over last 100 bars
            int n = 100, avail = _atr.Result.Count;
            if (n > avail) n = avail;
            double[] arr = new double[n];
            for (int i = 0; i < n; i++) arr[i] = _atr.Result.Last(i);
            System.Array.Sort(arr);
            double medAtr = arr[n / 2];

            // Cross within last 5 bars
            string cross = "none";
            for (int i = 1; i <= 5; i++)
            {
                double smaPrev = _sma.Result.Last(i);
                double smaCur  = _sma.Result.Last(i - 1);
                double emaPrev = _ema.Result.Last(i);
                double emaCur  = _ema.Result.Last(i - 1);
                if (smaPrev < emaPrev && smaCur > emaCur) { cross = "bull"; break; }
                if (smaPrev > emaPrev && smaCur < emaCur) { cross = "bear"; break; }
            }
            if (cross == "none")
                return Signal.Hold(cell.Bot, cell.Symbol, "no SMA/EMA cross in 5 bars");
            if (curAdx <= 15)
                return Signal.Hold(cell.Bot, cell.Symbol, "ADX too low");

            double conf;
            if (curAdx >= 25)      conf = 0.90;
            else if (curAdx >= 20) conf = 0.75;
            else                   conf = 0.60;
            if (curAtr < medAtr) conf = System.Math.Max(0.45, conf - 0.15);

            double close = bars.ClosePrices.LastValue;
            return new Signal
            {
                Bot = cell.Bot, Symbol = cell.Symbol,
                Direction = cross == "bull" ? Vote.Buy : Vote.Sell,
                Confidence = conf,
                BarTime = bars.OpenTimes.LastValue, Price = close,
                Atr = curAtr,
                Reason = string.Format("SMA9/EMA21 cross {0}, ADX={1:F1}", cross, curAdx)
            };
        }
    }
}
