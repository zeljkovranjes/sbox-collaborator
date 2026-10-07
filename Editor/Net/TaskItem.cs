using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class TaskItem
{
	public long Id { get; set; }
	public string ProjectId { get; set; }
	public string Title { get; set; }
	public string Description { get; set; }
	public string Status { get; set; }
	public string Priority { get; set; }
	public string OwnerId { get; set; }
	public string OwnerName { get; set; }
	public string AgentId { get; set; }
	public List<string> RelatedFiles { get; set; } = new();
	public List<string> RelatedAssets { get; set; } = new();
	public List<long> DependsOn { get; set; } = new();
	public string Branch { get; set; }
	public string BlockedReason { get; set; }
	public string CompletionSummary { get; set; }
	public string CreatedBy { get; set; }
	public DateTimeOffset? CreatedAt { get; set; }
	public DateTimeOffset? UpdatedAt { get; set; }
	public DateTimeOffset? CompletedAt { get; set; }
	public long Version { get; set; }
	public bool Stale { get; set; }

	/// <summary>The task's branch, or the one the server suggests (<c>task/42-boat-buoyancy</c>).</summary>
	public string SuggestedBranch { get; set; }

	/// <summary>The latest handoff note: where the previous owner got to.</summary>
	public TaskNote LastHandoff { get; set; }

	/// <summary>All notes (only filled by task_get).</summary>
	public List<TaskNote> Notes { get; set; } = new();

	public bool IsOpen => Status is not ("done");
	public bool IsActive => Status is "claimed" or "in_progress" or "blocked" or "review";
}
