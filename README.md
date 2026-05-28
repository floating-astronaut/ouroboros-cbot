# Ouroboros — multi-timeframe cTrader cBot

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-cTrader-blue.svg)](https://ctrader.com)
[![Language](https://img.shields.io/badge/lang-C%23%20%2F%20.NET%208-512BD4.svg)](https://dotnet.microsoft.com/)
[![GitHub stars](https://img.shields.io/github/stars/floating-astronaut/ouroboros-cbot?style=social)](https://github.com/floating-astronaut/ouroboros-cbot)

> A portfolio piece by [Nuraveda Lab](https://github.com/Nuraveda-Labs). Mirrored on
> [GitHub](https://github.com/floating-astronaut/ouroboros-cbot) ·
> [GitLab](https://gitlab.com/floating-astronaut/ouroboros-cbot) ·
> [Codeberg](https://codeberg.org/floating-astronaut/ouroboros-cbot).

Six ML bots — viper (M5), hydra (M1), mamba (M15), taipan (M30), cobra (H1),
anaconda (H4) — merged into one cBot through a data-driven whitelist derived
from 3,253 closed demo trades. Open-sourced as a portfolio piece showing how
to take a multi-bot ML signal layer and ship it as a single customer-side
`.algo` for the cTrader platform.

**Free to use.** Trade on a demo first; this is educational software, not
investment advice. See [LICENSE](LICENSE) for the full disclaimer.

## What it does

Each timeframe has its own strategy (RSI momentum, Bollinger-band reversion,
session-quality bias, MA-cross trend follower). Strategies run independently
on the symbols they have a proven historical edge on. When two or more bots
fire the same direction on the same symbol inside a 30-min window, the
position sizer scales the trade up — agreement multiplies *size*, not *entry
permission*, which is what the data actually showed.

Customer-side execution — the trader runs this in their own cTrader Desktop
on their broker account. Conservative drawdown caps are configurable.

## What's inside

| Bot      | Timeframe | Strategy           | Tradeable symbols (data-driven whitelist)            |
|----------|-----------|--------------------|------------------------------------------------------|
| hydra    | M1        | mamba_reversion    | JPN225, GER40, UK100, ETHUSD, XAUUSD, XAGUSD, EURJPY, EURUSD, AUDUSD, NZDUSD |
| viper    | M5        | momentum_hunter    | BTCUSD (only — bled $838k everywhere else)           |
| mamba    | M15       | mamba_reversion    | JPN225 (probation: small sample, 64% WR)             |
| taipan   | M30       | session_analyst    | GER40, XAUUSD, UK100, JPN225 (trend tier)            |
| cobra    | H1        | trend_follower     | GER40 (probation: small sample, 71% WR)              |
| anaconda | H4        | —                  | **disabled** (19% WR, structurally broken)           |

Whitelist generated from the live PostgreSQL `ml_trades` table — 3,253 closed
demo trades (2026-04-13 → 2026-05-12). Cells with ≥30 trades, +PnL and ≥45%
WR are `Full` tier. High-sample low-WR positive-tail strategies (viper-BTCUSD,
taipan-JPN225) are `Trend` tier (wider take-profit). Encouraging-but-thin
cells (mamba-JPN225, cobra-GER40) are `Probation` (½ size).

## Signal aggregation rule

The data shows multi-bot agreement does **not** improve win-rate, but **does**
multiply average PnL 4–9×. Therefore:

- Any whitelisted bot may enter on its own (no agreement gate).
- Position size is multiplied by the number of OTHER bots that fired the same
  direction on the same symbol within the last 30 min:
  - 0 agree → 1.0× base
  - 1 agree → 1.5× base
  - 2 agree → 2.0× base
  - 3+ agree → 2.5× base (cap)

Probation tier halves all of the above. There is no hierarchical TF veto —
H4 was the worst-performing TF in the data and would block profitable lower-TF
trades.

## Project layout

```
cbot/
├── Ouroboros.sln
└── Ouroboros/
    ├── Ouroboros.csproj
    ├── Ouroboros.cs               -- Robot class, parameters, OnStart, routing
    ├── Core/
    │   ├── Signal.cs              -- shared DTO + Vote enum
    │   ├── Whitelist.cs           -- compile-time bot×symbol×tier table
    │   ├── SignalBus.cs           -- rolling 30-min agreement memory
    │   ├── PositionSizer.cs       -- balance×risk%×tier×agreement → units
    │   └── RiskGuard.cs           -- daily/total DD caps + flatten switch
    └── Strategies/
        ├── IStrategy.cs           -- interface (Attach, OnBar→Signal)
        ├── MomentumHunter.cs      -- viper (RSI 48/52 cross + EMA + volume)
        ├── MambaReversion.cs      -- hydra & mamba (BB fade in ADX<25 ranges)
        ├── SessionAnalyst.cs      -- taipan (London/NY/overlap session + EMA slope)
        └── TrendFollower.cs       -- cobra (SMA9/EMA21 cross + ADX>15)
```

## Build

Local sanity-check with the .NET 8 SDK:

```bash
# from the repo root
dotnet build Ouroboros.sln -c Release
```

Output: `Ouroboros/bin/Release/net6.0/cbot.algo` — that's the file you import
into cTrader Desktop (Automate → cBots → Add → Import).

In normal use, customers don't run `dotnet build`. They drop the `.algo` (or
the source folder) into their cTrader Desktop's `cAlgo/Sources/Robots/`
directory and let cTrader compile it.

## Default parameters (cTrader UI groups)

**General** — `Label` (positions tagged `<Label>-<bot>-<symbol>`)

**Risk** (conservative drawdown presets)
- `Base risk per trade %` = **0.5**
- `Max daily drawdown %` = **3.5** (leaves headroom under a 4% account rule)
- `Max total drawdown %` = **5.5** (leaves headroom under a 6% account rule)
- `Flatten at daily DD %` = **4.0** (panic-flatten before the daily cap is breached)
- `Max concurrent positions` = **4**

**Execution**
- `Min confidence to enter` = **0.65**
- `ATR-fallback SL multiplier` = **1.5**
- `TP R-multiple (Full/Probation)` = **2.0**
- `TP R-multiple (Trend tier)` = **3.5** (wider — these strategies depend on tails)
- `Agreement window (minutes)` = **30**

**Bot enables** — kill switches per bot. Useful for paper-A/B testing one
allocation at a time.

## Logging

Every entry prints:

```
[Ouroboros] OPEN Buy GER40 0.42 units @ 18243.50 | SL=18215.40 TP=18299.70 | tier=Full agree=2 risk=$15.00
```

Risk blocks (drawdown caps, max concurrent) and below-minimum-volume skips
are also logged. Watch the cTrader Log tab.

## How this was built

1. Six Python ML bots ran on six separate cTrader demo accounts for ~30 days,
   one timeframe each (M1→H4), collecting OHLCV bars and demo-traded signals.
2. PostgreSQL captured 644k signals and 3,253 closed trades.
3. Each cell of the (bot × symbol) matrix was scored: trade count, win-rate,
   total PnL, median PnL. Only cells that cleared thresholds were promoted.
4. Multi-bot agreement was tested as a gate (no effect on WR) and as a sizer
   (4-9× avg PnL uplift). Sizer won.
5. The four surviving Python strategies were ported to C# (cTrader's API),
   the whitelist was embedded as a compile-time table, and the cBot was
   wired with a `SignalBus` (rolling agreement) + `PositionSizer`
   (risk-balance×tier×agreement) + `RiskGuard` (Zero DD caps).

## What's deliberately NOT in here

- **No HTTP/network calls.** Single `.algo` file, runs offline.
- **No ML inference at runtime.** All "ML" is baked into (a) the whitelist
  selection and (b) the strategy thresholds. Customers run pure C# logic.
- **No anaconda H4.** Data killed it.
- **No taipan-USDJPY / hydra-USDCHF / viper-everywhere-but-BTC.** Same.
- **No hierarchical TF veto.** Agreement is sizer-only.
