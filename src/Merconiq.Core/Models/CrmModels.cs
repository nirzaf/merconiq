using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>Opportunity count and expected value for one pipeline stage.</summary>
public sealed record CrmPipelineStageRow(CrmOpportunityStage Stage, int Count, decimal ExpectedValue);

/// <summary>Pipeline totals by stage; the open value covers opportunities still in Prospecting or Proposal.</summary>
public sealed record CrmPipelineSummary(IReadOnlyList<CrmPipelineStageRow> Stages, decimal OpenExpectedValue);
