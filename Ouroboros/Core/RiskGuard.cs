using System;
using cAlgo.API;

namespace cAlgo.Robots
{
    /// <summary>
    /// Drawdown safety net. Tracks intra-day high-water-mark equity and
    /// blocks new entries (and optionally flattens) when the configured
    /// drawdown caps are approached.
    /// </summary>
    public sealed class RiskGuard
    {
        public double MaxDailyDrawdownPct;     // halt new entries
        public double MaxTotalDrawdownPct;     // halt new entries + flatten
        public double FlattenAtDailyDrawdownPct; // hard kill switch

        private double _startOfDayEquity;
        private double _dayHighEquity;
        private double _startEquity;     // first OnStart equity
        private DateTime _currentDay;

        public RiskGuard(double maxDailyPct, double maxTotalPct, double flattenAtPct)
        {
            MaxDailyDrawdownPct      = maxDailyPct;
            MaxTotalDrawdownPct      = maxTotalPct;
            FlattenAtDailyDrawdownPct= flattenAtPct;
        }

        public void Init(double equity, DateTime now)
        {
            _startEquity = equity;
            _startOfDayEquity = equity;
            _dayHighEquity = equity;
            _currentDay = now.Date;
        }

        public void OnTick(double equity, DateTime now)
        {
            if (now.Date != _currentDay)
            {
                _currentDay = now.Date;
                _startOfDayEquity = equity;
                _dayHighEquity = equity;
            }
            if (equity > _dayHighEquity) _dayHighEquity = equity;
        }

        public double DailyDrawdownPct(double equity)
        {
            if (_startOfDayEquity <= 0) return 0;
            return 100.0 * (_startOfDayEquity - equity) / _startOfDayEquity;
        }

        public double TotalDrawdownPct(double equity)
        {
            if (_startEquity <= 0) return 0;
            return 100.0 * (_startEquity - equity) / _startEquity;
        }

        public bool CanEnter(double equity, out string reason)
        {
            double dd  = DailyDrawdownPct(equity);
            double tdd = TotalDrawdownPct(equity);
            if (dd >= MaxDailyDrawdownPct) { reason = string.Format("daily DD {0:F2}% >= cap {1:F2}%", dd, MaxDailyDrawdownPct); return false; }
            if (tdd >= MaxTotalDrawdownPct) { reason = string.Format("total DD {0:F2}% >= cap {1:F2}%", tdd, MaxTotalDrawdownPct); return false; }
            reason = string.Empty;
            return true;
        }

        public bool ShouldFlatten(double equity)
        {
            return DailyDrawdownPct(equity) >= FlattenAtDailyDrawdownPct
                || TotalDrawdownPct(equity) >= MaxTotalDrawdownPct;
        }
    }
}
