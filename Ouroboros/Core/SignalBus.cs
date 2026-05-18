using System;
using System.Collections.Generic;
using System.Linq;

namespace cAlgo.Robots
{
    /// <summary>
    /// Rolling-window memory of recent signals across all 6 bots.
    /// Used by the sizer to count how many OTHER bots agreed same-side on the
    /// same symbol within the agreement window before this entry.
    ///
    /// Data finding (3253 closed trades): agreement does NOT improve win-rate,
    /// but average PnL jumps 4-9x as more bots align. So agreement scales SIZE,
    /// not entry permission.
    /// </summary>
    public sealed class SignalBus
    {
        private readonly TimeSpan _window;
        private readonly List<Signal> _signals = new List<Signal>();

        public SignalBus(TimeSpan window) { _window = window; }

        public void Record(Signal s, DateTime now)
        {
            if (s.Direction == Vote.Hold) return;
            _signals.Add(new Signal {
                Bot = s.Bot, Symbol = s.Symbol, Direction = s.Direction,
                Confidence = s.Confidence, BarTime = now
            });
            Prune(now);
        }

        public int AgreementCount(string symbol, Vote dir, string excludeBot, DateTime now)
        {
            Prune(now);
            var bots = new HashSet<string>();
            foreach (var s in _signals)
            {
                if (s.Symbol != symbol) continue;
                if (s.Direction != dir) continue;
                if (s.Bot == excludeBot) continue;
                bots.Add(s.Bot);
            }
            return bots.Count;
        }

        private void Prune(DateTime now)
        {
            DateTime cutoff = now - _window;
            int i = 0;
            while (i < _signals.Count && _signals[i].BarTime < cutoff) i++;
            if (i > 0) _signals.RemoveRange(0, i);
        }
    }
}
