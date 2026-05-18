using System;

namespace cAlgo.Robots
{
    public enum Vote { Hold = 0, Buy = 1, Sell = -1 }

    public sealed class Signal
    {
        public string Bot;          // hydra | viper | mamba | taipan | cobra
        public string Symbol;       // EURUSD, BTCUSD, ...
        public Vote   Direction;
        public double Confidence;   // 0..1
        public DateTime BarTime;    // bar that produced the signal
        public double  Price;       // close at signal time
        public double? SuggestedSl; // strategy-provided stop, null = use ATR default
        public double? SuggestedTp; // strategy-provided target, null = use ATR default
        public double  Atr;         // ATR at signal time (for default stop/target)
        public string  Reason;      // human-readable for logs

        public static Signal Hold(string bot, string symbol, string reason)
        {
            return new Signal
            {
                Bot = bot, Symbol = symbol, Direction = Vote.Hold,
                Confidence = 0.0, Reason = reason
            };
        }
    }
}
