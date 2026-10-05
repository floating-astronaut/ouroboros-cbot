// -----------------------------------------------------------------------------
//  Ouroboros — Tejas Karan Agrawal
//  Six ML bots (viper M5, hydra M1, mamba M15, taipan M30, cobra H1, anaconda H4)
//  merged into one cBot. Strategies are C# ports of the Python originals; the
//  bot×symbol whitelist is compiled from 3,253 closed demo trades (positive
//  cells only). Agreement scales position size, not entry permission.
//
//  Customer-side execution model — runs in the trader's cTrader Desktop on
//  their own broker account. Conservative drawdown caps are configurable
//  below for use with strict-rules accounts.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None, AddIndicators = true)]
    public class Ouroboros : Robot
    {
        // ============================================================
        //  Parameters — visible in cTrader's cBot config UI
        // ============================================================

        [Parameter("Label", DefaultValue = "Ouroboros", Group = "General")]
        public string Label { get; set; }

        // ---- Risk ----
        [Parameter("Base risk per trade %", DefaultValue = 0.5, MinValue = 0.05, MaxValue = 5.0, Group = "Risk")]
        public double BaseRiskPct { get; set; }

        [Parameter("Max daily drawdown %", DefaultValue = 3.5, MinValue = 0.5, MaxValue = 10, Group = "Risk")]
        public double MaxDailyDrawdownPct { get; set; }

        [Parameter("Max total drawdown %", DefaultValue = 5.5, MinValue = 1, MaxValue = 20, Group = "Risk")]
        public double MaxTotalDrawdownPct { get; set; }

        [Parameter("Flatten at daily DD %", DefaultValue = 4.0, MinValue = 1, MaxValue = 10, Group = "Risk")]
        public double FlattenAtDailyDrawdownPct { get; set; }

        [Parameter("Max concurrent positions", DefaultValue = 4, MinValue = 1, MaxValue = 20, Group = "Risk")]
        public int MaxConcurrentPositions { get; set; }

        // ---- Per-trade execution ----
        [Parameter("Min confidence to enter", DefaultValue = 0.65, MinValue = 0.5, MaxValue = 0.95, Group = "Execution")]
        public double MinConfidence { get; set; }

        [Parameter("ATR-fallback SL multiplier", DefaultValue = 1.5, MinValue = 0.5, MaxValue = 5, Group = "Execution")]
        public double AtrSlMult { get; set; }

        [Parameter("TP R-multiple (Full/Probation)", DefaultValue = 2.0, MinValue = 0.5, MaxValue = 10, Group = "Execution")]
        public double TpRMultipleNormal { get; set; }

        [Parameter("TP R-multiple (Trend tier)", DefaultValue = 3.5, MinValue = 1, MaxValue = 15, Group = "Execution")]
        public double TpRMultipleTrend { get; set; }

        [Parameter("Agreement window (minutes)", DefaultValue = 30, MinValue = 1, MaxValue = 240, Group = "Execution")]
        public int AgreementWindowMin { get; set; }

        // ---- Bot kill-switches (turn individual bots off without recompile) ----
        [Parameter("Enable hydra (M1)",   DefaultValue = true,  Group = "Bot enables")]
        public bool EnableHydra   { get; set; }
        [Parameter("Enable viper (M5)",   DefaultValue = true,  Group = "Bot enables")]
        public bool EnableViper   { get; set; }
        [Parameter("Enable mamba (M15)",  DefaultValue = true,  Group = "Bot enables")]
        public bool EnableMamba   { get; set; }
        [Parameter("Enable taipan (M30)", DefaultValue = true,  Group = "Bot enables")]
        public bool EnableTaipan  { get; set; }
        [Parameter("Enable cobra (H1)",   DefaultValue = true,  Group = "Bot enables")]
        public bool EnableCobra   { get; set; }
        // anaconda excluded — kill-listed by data (19% WR, structurally broken).

        // ============================================================
        //  Internal state
        // ============================================================

        private SignalBus _bus;
        private RiskGuard _risk;

        // One (cell, strategy, bars) tuple per active whitelist row.
        private sealed class Slot
        {
            public Cell Cell;
            public IStrategy Strategy;
            public Bars Bars;
            public Symbol Symbol;
        }
        private readonly List<Slot> _slots = new List<Slot>();

        // ============================================================
        //  Lifecycle
        // ============================================================

        protected override void OnStart()
        {
            _bus  = new SignalBus(TimeSpan.FromMinutes(AgreementWindowMin));
            _risk = new RiskGuard(MaxDailyDrawdownPct, MaxTotalDrawdownPct, FlattenAtDailyDrawdownPct);
            _risk.Init(Account.Equity, Server.Time);

            int wired = 0, skipped = 0;
            foreach (var cell in Whitelist.Cells)
            {
                if (!BotEnabled(cell.Bot)) { skipped++; continue; }

                Symbol sym = Symbols.GetSymbol(cell.Symbol);
                if (sym == null)
                {
                    Print("[Ouroboros] skip {0}-{1}: symbol not available on this broker", cell.Bot, cell.Symbol);
                    skipped++;
                    continue;
                }

                Bars bars = MarketData.GetBars(cell.TimeFrame, cell.Symbol);
                IStrategy strat = NewStrategy(cell.StrategyId);
                if (strat == null)
                {
                    Print("[Ouroboros] skip {0}-{1}: unknown strategy '{2}'", cell.Bot, cell.Symbol, cell.StrategyId);
                    skipped++;
                    continue;
                }
                strat.Attach(this, bars, cell);

                var slot = new Slot { Cell = cell, Strategy = strat, Bars = bars, Symbol = sym };
                _slots.Add(slot);

                // Per-cell bar-closed handler.
                Cell captured = cell;
                IStrategy capturedStrat = strat;
                Bars capturedBars = bars;
                Symbol capturedSym = sym;
                bars.BarClosed += args => OnCellBarClosed(captured, capturedStrat, capturedBars, capturedSym);
                wired++;
            }

            Print("[Ouroboros] started — {0} cells wired, {1} skipped. Equity={2:F2} {3}",
                wired, skipped, Account.Equity, Account.Asset.Name);
        }

        protected override void OnTick()
        {
            _risk.OnTick(Account.Equity, Server.Time);
            if (_risk.ShouldFlatten(Account.Equity))
            {
                FlattenAll("risk-guard hard flatten");
            }
        }

        protected override void OnStop()
        {
            Print("[Ouroboros] stopped. Equity={0:F2} {1}, day-DD={2:F2}%, total-DD={3:F2}%",
                Account.Equity, Account.Asset.Name,
                _risk.DailyDrawdownPct(Account.Equity),
                _risk.TotalDrawdownPct(Account.Equity));
        }

        // ============================================================
        //  Per-cell bar handler
        // ============================================================

        private void OnCellBarClosed(Cell cell, IStrategy strat, Bars bars, Symbol sym)
        {
            Signal sig;
            try { sig = strat.OnBar(this, bars, cell); }
            catch (Exception ex)
            {
                Print("[Ouroboros] {0}-{1} strategy error: {2}", cell.Bot, cell.Symbol, ex.Message);
                return;
            }

            if (sig == null || sig.Direction == Vote.Hold) return;
            if (sig.Confidence < MinConfidence) return;

            int agreement = _bus.AgreementCount(cell.Symbol, sig.Direction, cell.Bot, Server.Time);
            _bus.Record(sig, Server.Time);

            string reason;
            if (!_risk.CanEnter(Account.Equity, out reason))
            {
                Print("[Ouroboros] {0}-{1} blocked: {2}", cell.Bot, cell.Symbol, reason);
                return;
            }
            if (CountOpenPositions() >= MaxConcurrentPositions)
            {
                Print("[Ouroboros] {0}-{1} blocked: max concurrent positions hit", cell.Bot, cell.Symbol);
                return;
            }
            // One bot may not have two stacked positions on the same symbol.
            if (PositionExists(cell.Bot, cell.Symbol)) return;

            var plan = PositionSizer.Build(sig, cell, sym, Account.Equity,
                BaseRiskPct, agreement, AtrSlMult, TpRMultipleNormal, TpRMultipleTrend);

            if (plan.VolumeUnits <= 0)
            {
                Print("[Ouroboros] {0}-{1} skipped: volume below broker minimum (risk=${2:F2})",
                    cell.Bot, cell.Symbol, plan.RiskDollars);
                return;
            }

            TradeType side = sig.Direction == Vote.Buy ? TradeType.Buy : TradeType.Sell;
            string label  = string.Format("{0}-{1}-{2}", Label, cell.Bot, cell.Symbol);
            string comment= string.Format("agree={0} conf={1:F2} tier={2} reason={3}",
                                          agreement, sig.Confidence, cell.Tier, sig.Reason);

            var result = ExecuteMarketOrder(side, cell.Symbol, plan.VolumeUnits, label,
                                            null /* SL set via modify */, null,
                                            comment);
            if (result.IsSuccessful && result.Position != null)
            {
                ModifyPosition(result.Position, plan.StopLossPrice, plan.TakeProfitPrice, ProtectionType.Absolute);
                Print("[Ouroboros] OPEN {0} {1} {2:F2} units @ {3:F5} | SL={4:F5} TP={5:F5} | tier={6} agree={7} risk=${8:F2}",
                    side, cell.Symbol, plan.VolumeUnits, sig.Price,
                    plan.StopLossPrice, plan.TakeProfitPrice,
                    cell.Tier, agreement, plan.RiskDollars);
            }
            else
            {
                Print("[Ouroboros] OPEN FAILED {0}-{1}: {2}",
                    cell.Bot, cell.Symbol, result.Error.HasValue ? result.Error.Value.ToString() : "?");
            }
        }

        // ============================================================
        //  Helpers
        // ============================================================

        private IStrategy NewStrategy(string id)
        {
            switch (id)
            {
                case "momentum_hunter": return new MomentumHunter();
                case "mamba_reversion": return new MambaReversion();
                case "session_analyst": return new SessionAnalyst();
                case "trend_follower":  return new TrendFollower();
                default: return null;
            }
        }

        private bool BotEnabled(string bot)
        {
            switch (bot)
            {
                case "hydra":   return EnableHydra;
                case "viper":   return EnableViper;
                case "mamba":   return EnableMamba;
                case "taipan":  return EnableTaipan;
                case "cobra":   return EnableCobra;
                default:        return false;
            }
        }

        private bool PositionExists(string bot, string symbol)
        {
            string label = string.Format("{0}-{1}-{2}", Label, bot, symbol);
            foreach (var p in Positions)
                if (p.Label == label) return true;
            return false;
        }

        private int CountOpenPositions()
        {
            int n = 0;
            foreach (var p in Positions)
                if (p.Label != null && p.Label.StartsWith(Label)) n++;
            return n;
        }

        private void FlattenAll(string reason)
        {
            int closed = 0;
            foreach (var p in Positions)
            {
                if (p.Label == null || !p.Label.StartsWith(Label)) continue;
                ClosePosition(p);
                closed++;
            }
            if (closed > 0) Print("[Ouroboros] FLATTEN ALL ({0} positions) — {1}", closed, reason);
        }
    }
}
