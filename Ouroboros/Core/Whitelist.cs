using System;
using System.Collections.Generic;
using cAlgo.API;

namespace cAlgo.Robots
{
    // Tier controls per-trade sizing:
    //   Full      — proven cell: WR >= 45%, n >= 30, +PnL                            → 1.00 x base risk
    //   Trend     — high-sample low-WR positive-tail: n >= 100, WR >= 40%, +PnL      → 1.00 x base risk, wider TP
    //   Probation — small sample but encouraging: 10 <= n < 30, WR >= 60%, +PnL      → 0.50 x base risk
    public enum Tier { Full, Trend, Probation }

    public sealed class Cell
    {
        public string Bot;          // viper | hydra | mamba | taipan | cobra
        public string Symbol;       // EURUSD, BTCUSD, ...
        public TimeFrame TimeFrame; // M1 / M5 / M15 / M30 / H1
        public string StrategyId;   // momentum_hunter / mamba_reversion / session_analyst / trend_follower
        public Tier   Tier;
        public int    Trades;       // historical sample size
        public double WinRate;      // historical, 0..1
        public double NetPnL;       // historical demo PnL (USD)
    }

    public static class Whitelist
    {
        // Compiled from PostgreSQL ml_trades (3,253 closed trades, 2026-04-13..2026-05-12).
        // Anaconda H4 entirely killed (19% WR, structurally broken on 43-trade sample).
        // Viper M5 killed everywhere except BTCUSD (-$838k everywhere else, +$475k on BTC).
        // Taipan-JPN225 retained but flagged Trend — the +$2M comes from one Nikkei mega-day.
        public static readonly IReadOnlyList<Cell> Cells = new List<Cell>
        {
            // ---- HYDRA (M1, mamba_reversion) — broad coverage, mean reversion on M1 ranges ----
            New("hydra", "JPN225", TimeFrame.Minute,  "mamba_reversion", Tier.Full,      119, 0.454,   62260),
            New("hydra", "GER40",  TimeFrame.Minute,  "mamba_reversion", Tier.Full,       81, 0.469,   21145),
            New("hydra", "UK100",  TimeFrame.Minute,  "mamba_reversion", Tier.Full,       75, 0.467,   10280),
            New("hydra", "ETHUSD", TimeFrame.Minute,  "mamba_reversion", Tier.Full,       32, 0.594,    4844),
            New("hydra", "XAUUSD", TimeFrame.Minute,  "mamba_reversion", Tier.Full,       91, 0.527,    3591),
            New("hydra", "XAGUSD", TimeFrame.Minute,  "mamba_reversion", Tier.Full,       79, 0.494,     282),
            New("hydra", "EURJPY", TimeFrame.Minute,  "mamba_reversion", Tier.Full,       77, 0.584,     126),
            New("hydra", "EURUSD", TimeFrame.Minute,  "mamba_reversion", Tier.Full,       67, 0.493,      30),
            New("hydra", "AUDUSD", TimeFrame.Minute,  "mamba_reversion", Tier.Full,       88, 0.534,       1),
            New("hydra", "NZDUSD", TimeFrame.Minute,  "mamba_reversion", Tier.Full,       82, 0.476,       0),

            // ---- VIPER (M5, momentum_hunter) — BTC only; everywhere else lost money ----
            New("viper", "BTCUSD", TimeFrame.Minute5, "momentum_hunter", Tier.Trend,     193, 0.420,  474606),

            // ---- MAMBA (M15, mamba_reversion) — probation: small sample, but 64% WR ----
            New("mamba", "JPN225", TimeFrame.Minute15,"mamba_reversion", Tier.Probation,  11, 0.636,   74660),

            // ---- TAIPAN (M30, session_analyst) — GER40 is the real edge ----
            New("taipan","GER40",  TimeFrame.Minute30,"session_analyst", Tier.Full,       60, 0.533,  149830),
            New("taipan","XAUUSD", TimeFrame.Minute30,"session_analyst", Tier.Full,       64, 0.484,   18037),
            New("taipan","UK100",  TimeFrame.Minute30,"session_analyst", Tier.Full,       44, 0.455,   12920),
            // Trend tier — the famous Apr-21 Nikkei spike; 41% WR means most trades lose, tail wins
            New("taipan","JPN225", TimeFrame.Minute30,"session_analyst", Tier.Trend,      34, 0.412, 2043140),

            // ---- COBRA (H1, trend_follower) — probation: 14 trades on GER40 at 71% WR ----
            New("cobra", "GER40",  TimeFrame.Hour,    "trend_follower",  Tier.Probation,  14, 0.714,   67780),
        };

        private static Cell New(string bot, string sym, TimeFrame tf, string strat, Tier tier,
                                int n, double wr, double pnl)
        {
            return new Cell { Bot = bot, Symbol = sym, TimeFrame = tf,
                              StrategyId = strat, Tier = tier,
                              Trades = n, WinRate = wr, NetPnL = pnl };
        }

        // Sizing multiplier from Tier (base risk per trade × this).
        public static double TierMultiplier(Tier t)
        {
            switch (t)
            {
                case Tier.Full:      return 1.00;
                case Tier.Trend:     return 1.00;
                case Tier.Probation: return 0.50;
                default:             return 0.00;
            }
        }
    }
}
