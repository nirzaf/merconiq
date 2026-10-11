# Demand forecasting runtime and evaluation

Merconiq provides native on-device demand forecasting built into the platform. This document specifies the runtime architecture, algorithm implementations, configuration limits, and chronological evaluation methods.

---

## 1. Overview and Model Selection

The production Docker image targets both `linux/amd64` and `linux/arm64`. Merconiq supports two forecasting engines:

1. **`managed-moving-average` (Default):**
   - A deterministic, platform-independent algorithm implemented in pure C#.
   - Does not load external or native C++ ML libraries, guaranteeing 100% portability across all operating systems and architectures.
   - Calculates the mean daily sell quantity over a bounded historical window and projects it forward across the requested forecast horizon.

2. **`ssa` (Singular Spectrum Analysis via ML.NET):**
   - An explicit opt-in time-series model leveraging `Microsoft.ML.TimeSeries`.
   - Decomposes time series into trend, seasonal, and noise components.
   - Requires compatible native runtime libraries on target deployment platforms.

Forecasting is enabled by default. To disable it, set `Forecasting:Enabled=false` (or `Forecasting__Enabled=false` in environment variables). When disabled:
- All forecast API endpoints return HTTP 503 Service Unavailable.
- Scheduled background tenant forecasting tasks skip execution without enumerating tenants.
- No third-party or external cloud AI services are ever called; all computations occur entirely within the local application database boundary.

---

## 2. Configuration and Hard Limits

Forecast requests are strictly bounded by configuration to prevent resource exhaustion and unbounded queries:

| Configuration Setting | Default | Supported Range | Description and Invariants |
|---|---:|---:|---|
| `Forecasting:Enabled` | `true` | `true` / `false` | Master toggle. When false, endpoints return HTTP 503 and background work is halted. |
| `Forecasting:Implementation` | `managed-moving-average` | `managed-moving-average`, `ssa` | Engine selection. Validated on startup. |
| `Forecasting:MaxForecastHorizonDays` | 90 days | 1–365 days | Maximum forecast horizon. Requests requesting beyond this limit return an HTTP 400 validation error (never silently clamped). |
| `Forecasting:MaxHistoricalDays` | 365 days | 5–3,650 days | Inclusive historical calendar window ending at UTC today. |
| `Forecasting:MaxHistoricalTransactionsPerForecast` | 50,000 | 1–250,000 | Maximum matching sell transactions read per forecast. If the limit is exceeded, the request is rejected with HTTP 400 rather than truncated. |
| `Forecasting:MaxItemsPerAllItemsForecast` | 250 | 1–2,500 | Tenant-filtered item catalog cap for bulk all-item forecast queries. Rejects requests exceeding the limit with HTTP 400. |

### Metadata in Responses
Every forecast response explicitly returns audit metadata:
- `ForecastingImplementation`: (`managed-moving-average` or `ssa`).
- `ForecastingImplementationVersion`: `1.0.0` for managed, or the loaded assembly version for SSA.
- `DataWindowStartDate` / `DataWindowEndDate`: The exact historical observation window used.
- `ConfiguredLimits`: The active configuration thresholds.
- `KnownLimitations`: Algorithmic caveats (e.g. absence of lost-sales tracking, zero-sale gap handling).

---

## 3. Data Processing and Invariants

1. **Daily Series Aggregation:** Daily demand aggregates only physical sales movements (`StockTransactionType.Sell`). Receipts, purchase orders, reservations, and internal warehouse transfers do not constitute demand.
2. **Missing Date Imputation:** Missing dates between the first and latest observed sales dates within the window are zero-filled. Dates following the latest sale are not zero-padded.
3. **No Phantom Lost Sales:** Merconiq does not infer unobserved stockout demand; zero observed sales reflect zero recorded demand.
4. **Tenant Isolation:** All data selection queries strictly enforce tenant scoping and soft-delete exclusions before applying database limits.

---

## 4. Chronological Holdout Evaluation

The fixture `ForecastingEvaluation_UsesChronologicalHoldoutAndReportsBaseline` in `tests/Merconiq.Tests/Core/Services/DemandForecastServiceTests.cs` validates forecasting behavior using an 8-day chronological dataset:

| Split | Values | Count | Role |
|---|---|---:|---|
| **Training Set** | 4, 6, 8, 10, 12 | 5 | Observed historical inputs |
| **Holdout Set** | 14, 16, 18 | 3 | Future observations evaluated strictly after prediction |

- **Managed Moving-Average Prediction:** Training mean = `8`.
- **Naive Baseline:** Last observed value = `12`.
- **Holdout Evaluation:** Mean absolute error (MAE) is measured strictly against the future holdout without data leakage from the future into the past.

To execute the forecasting test suite:
```bash
dotnet test tests/Merconiq.Tests/Merconiq.Tests.csproj \
  --configuration Release \
  --filter "FullyQualifiedName~DemandForecastServiceTests"
```
