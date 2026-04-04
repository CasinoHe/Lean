# AGENTS.md

This file applies inside `Lean/`. Read `../Docs/fork-notes.md` for the detailed mechanism reference.

## Purpose

- This Lean submodule contains intentional runtime behavior changes used by the parent trading system.
- Treat upstream Lean docs and upstream Lean examples as useful background, not as the final source of truth for this checkout.

## Key Runtime Differences

- Additional per-security subscriptions are supported through `AddSecuritySubscription(...)`.
- Extra subscriptions are attached to universe ownership and are expected to be cleaned up with the selecting universe.
- Subscription start-time handling has been customized for live mode, backtest mode, and future-dated subscription starts.
- Consolidator registration and subscription selection are customized for multi-timeframe workflows.
- Internal feeds may participate in consolidator/subscription selection.
- Live history can use a hybrid local-file-plus-brokerage provider instead of a single-source model.

## Config-Sensitive Behavior

- `disable-equity-quotes` can remove default equity quote subscriptions.
- `neutralize-splits` can force split factors to `1.0` during factor-file parsing.
- JSON config files may contain comments because config parsing was relaxed to allow them.
- `ib-client-id` is part of the Lean-side IBKR configuration surface.

## Working Guidance

- Before changing subscription, consolidator, or history-provider code, verify whether the current behavior is a fork feature rather than an accidental divergence.
- When debugging strategy behavior, check forked subscription mechanics before assuming the algorithm code is wrong.
- Keep mechanism explanations in `../Docs/fork-notes.md`; keep this file short and operational.
