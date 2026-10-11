namespace Merconiq.Core.Entities;

/// <summary>Pipeline stage. Won and Lost are terminal; open stages move forward or close.</summary>
public enum CrmOpportunityStage
{
    Prospecting = 0,
    Proposal = 1,
    Won = 2,
    Lost = 3
}
