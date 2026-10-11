---
layout: default
title: Merconiq Documentation
description: Complete enterprise documentation for the Merconiq .NET 10 / PostgreSQL ERP platform.
---

# 📦 Merconiq Documentation

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/nirzaf/merconiq/blob/master/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-purple.svg)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-blue.svg)](https://www.postgresql.org/)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ED.svg)](https://www.docker.com/)

Welcome to the documentation portal for **Merconiq** — a modern, native **.NET 10 / EF Core / PostgreSQL** enterprise ERP platform with complete functional parity against **ERPNext v16**.

> The canonical documentation URL is hosted on [GitHub Pages](https://nirzaf.github.io/merconiq/).

---

## 📖 Documentation Index

### 1. Enterprise Core & Architecture
| Document | Audience | Focus Area |
|---|---|---|
| **[Architecture Boundaries](ARCHITECTURE.md)** | Developers & Architects | Modular-monolith structure, project dependency graph, analyzer baselines, OpenTelemetry observability, and Data Protection key persistence. |
| **[ERPNext Parity Matrix](ERPNEXT_PARITY.md)** | Product Owners & Auditors | Comprehensive 100% parity breakdown against ERPNext v16 backed by executable PostgreSQL integration test citations. |
| **[Finance Posting Kernel](FINANCE_POSTING.md)** | Finance Engineers | Double-entry balanced immutable journals, source-action idempotency, period 13 adjustments, fiscal locks, and trial balance reconciliation. |
| **[Inventory Valuation & Costing](INVENTORY_VALUATION.md)** | Inventory Controllers | Moving-average stock valuation math, FIFO layer queues, Landed Cost Vouchers (LCV), and append-only stock ledgers. |
| **[Authorization & Capabilities](AUTHORIZATION_MATRIX.md)** | Security Administrators | Multi-tenant isolation, role-based access control (RBAC), company capability gates, and audit trails. |
| **[Organization Ownership](ORGANIZATION_OWNERSHIP.md)** | System Administrators | Multi-tenant organization hierarchies, legal companies, branches, warehouses, and document ownership boundaries. |
| **[Acceptance Scenarios](ACCEPTANCE_SCENARIOS.md)** | QA & Engineers | Canonical golden test fixtures and end-to-end integration workflows. |

### 2. Operations & Guides
| Document | Audience | Focus Area |
|---|---|---|
| **[User Guide](USER_GUIDE.md)** | End Users & Operators | Day-to-day operations manual covering inventory, POS, procurement, sales, manufacturing, payroll, and banking. |
| **[REST API Reference](API.md)** | Integrators & Developers | Versioned `/api/v1` endpoints, HMAC webhook subscriptions, idempotent requests, and payload specifications. |
| **[Backup & Recovery Runbook](BACKUP_RESTORE.md)** | DevOps & DBAs | PostgreSQL logical dump/restore procedures, verification checklists, and Data Protection encryption key backups. |
| **[Transfer Transit Runbook](TRANSFER_TRANSIT_RUNBOOK.md)** | Warehouse Managers | In-transit stock transfer tracking, aging reconciliation, and discrepancy dispute resolution. |
| **[Master Data Onboarding](MASTER_DATA_ONBOARDING.md)** | Implementers | Bulk onboarding specifications and CSV/Excel import templates for catalog items, suppliers, customers, and opening balances. |

### 3. AI & Demand Forecasting
| Document | Audience | Focus Area |
|---|---|---|
| **[Demand Forecasting Engine](FORECASTING.md)** | Data Scientists & Engineers | Native on-device forecasting runtime (deterministic Managed Moving Average & ML.NET SSA), configuration ceilings, and chronological holdout evaluation. |

### 4. Security, Provenance & Legal
| Document | Audience | Focus Area |
|---|---|---|
| **[Security Scanning & Headers](SECURITY_SCANNING.md)** | Security Engineers | Automated CodeQL static analysis, container vulnerability scanning with Trivy SARIF reporting, and defense-in-depth HTTP security headers (CSP, HSTS). |
| **[Threat Model](THREAT_MODEL.md)** | Security Architects | STRIDE threat matrix, attack surface evaluation, and boundary mitigations. |
| **[Image Provenance & Attestations](IMAGE_PROVENANCE.md)** | SecOps & Release Managers | Multi-architecture container build provenance, BuildKit SBOM attestations, and immutable digest verification. |
| **[Project Provenance](PROVENANCE.md)** | Maintainers | Repository history integrity, authentic commit policies, and contributor guidelines. |
| **[Third-Party Notices](THIRD_PARTY_NOTICES.md)** | Compliance Officers | Full inventory of third-party open-source components and licenses. |

---

## 🚀 Quick Start

Launch Merconiq locally using Docker Compose:

```bash
git clone https://github.com/nirzaf/merconiq.git
cd merconiq
cp .env.example .env        # Set database credentials and JWT signing key
./scripts/validate-compose.sh development
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --wait
```

The application will be live at **http://localhost:8080** with Swagger API documentation available at **http://localhost:8080/swagger**.

---

## 🧭 Navigation

- **Using the application** → [User Guide](USER_GUIDE.md)
- **API integrations** → [REST API Reference](API.md)
- **ERP feature verification** → [ERPNext Parity Matrix](ERPNEXT_PARITY.md)
- **Architecture design** → [Architecture Boundaries](ARCHITECTURE.md)
- **Disaster recovery** → [Backup & Recovery Runbook](BACKUP_RESTORE.md)
- **Source repository** → [GitHub Repository](https://github.com/nirzaf/merconiq)

---

*Built with .NET 10, ASP.NET Core, MudBlazor, PostgreSQL 16, MediatR, and QuestPDF. Released under the [MIT License](https://github.com/nirzaf/merconiq/blob/master/LICENSE).*
