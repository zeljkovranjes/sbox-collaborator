using System.Text.Json;

namespace Collaborator.Net;

// Shapes of the server's entities (docs/http-api.md). Only the fields the editor shows are
// declared; unknown fields are ignored. Timestamps stay nullable so a missing one never fails a read.

public sealed class ServerInfo
{
	public string Name { get; set; }
	public string Version { get; set; }
	public bool GithubLogin { get; set; }
	public bool KeyLogin { get; set; }
}

public sealed class ServerKeyCheck
{
	public bool Valid { get; set; }
	public string ServerName { get; set; }
}

public sealed class DeviceStart
{
	public string DeviceCode { get; set; }
	public string UserCode { get; set; }
	public string VerificationUri { get; set; }
	public string VerificationUriComplete { get; set; }
	public int Interval { get; set; } = 5;
	public int ExpiresIn { get; set; } = 600;
}

public sealed class DeviceToken
{
	public string Token { get; set; }
	public Developer Developer { get; set; }
	public AccessKey Key { get; set; }
}

public sealed class Me
{
	public Developer Developer { get; set; }
	public AccessKey Key { get; set; }
	public List<string> Scopes { get; set; } = new();
	public List<string> ProjectIds { get; set; }

	public bool CanWrite => Scopes.Contains( "write" ) || Scopes.Contains( "admin" );
}

public sealed class Developer
{
	public string Id { get; set; }
	public string DisplayName { get; set; }
	public string GithubLogin { get; set; }
	public string Role { get; set; }
	public bool Online { get; set; }

	public string Name => string.IsNullOrEmpty( DisplayName ) ? Id : DisplayName;
}

public sealed class AccessKey
{
	public string Id { get; set; }
	public string Name { get; set; }
	public string Prefix { get; set; }
	public List<string> Scopes { get; set; } = new();
}

public sealed class CollabProject
{
	public string Id { get; set; }
	public string Name { get; set; }
	public string Kind { get; set; }
	public string PackageIdent { get; set; }
	public string DefaultBranch { get; set; }
	public string Summary { get; set; }
	public string Milestone { get; set; }
	public string Conventions { get; set; }

	public string Title => string.IsNullOrEmpty( Name ) ? Id : Name;
}

public sealed class Agent
{
	public string Id { get; set; }
	public string DeveloperId { get; set; }
	public string DeveloperName { get; set; }
	public string ProjectId { get; set; }
	public string ClientType { get; set; }
	public string Machine { get; set; }
	public string Model { get; set; }
	public string Label { get; set; }
	public string Status { get; set; }
	public string StatusNote { get; set; }
	public long? CurrentTaskId { get; set; }
	public string CurrentTaskTitle { get; set; }
	public string Branch { get; set; }
	public List<string> Files { get; set; } = new();
	public DateTimeOffset? StartedAt { get; set; }
	public DateTimeOffset? LastHeartbeatAt { get; set; }
	public bool Online { get; set; }

	public string DisplayLabel => !string.IsNullOrEmpty( Label ) ? Label : ClientType ?? "agent";
}

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

	public bool IsOpen => Status is not ("done");
	public bool IsActive => Status is "claimed" or "in_progress" or "blocked" or "review";
}

public sealed class Reservation
{
	public string Id { get; set; }
	public string ProjectId { get; set; }
	public string Path { get; set; }
	public bool IsDirectory { get; set; }
	public string DeveloperId { get; set; }
	public string DeveloperName { get; set; }
	public string AgentId { get; set; }
	public string AgentLabel { get; set; }
	public long? TaskId { get; set; }
	public string TaskTitle { get; set; }
	public string Reason { get; set; }
	public string Branch { get; set; }
	public DateTimeOffset? CreatedAt { get; set; }
	public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class Conflict
{
	public string Path { get; set; }
	public string Relation { get; set; }
	public Reservation Reservation { get; set; }
	public string Message { get; set; }
}

public sealed class ReserveResult
{
	public List<Reservation> Reserved { get; set; } = new();
	public List<Conflict> Conflicts { get; set; } = new();
	public string Message { get; set; }
}

public sealed class ConflictCheck
{
	public bool Clear { get; set; }
	public List<Conflict> Conflicts { get; set; } = new();
	public string Message { get; set; }
}

public sealed class ReleaseResult
{
	public int Released { get; set; }
}

public sealed class Activity
{
	public string Id { get; set; }
	public string ProjectId { get; set; }
	public DateTimeOffset? At { get; set; }
	public string ActorId { get; set; }
	public string ActorName { get; set; }
	public string AgentId { get; set; }
	public string AgentLabel { get; set; }
	public string Kind { get; set; }
	public string Summary { get; set; }
	public string RefType { get; set; }
	public string RefId { get; set; }
	public int Importance { get; set; }
}

public sealed class TeamMessage
{
	public long Id { get; set; }
	public string ProjectId { get; set; }
	public string Type { get; set; }
	public string FromDeveloperId { get; set; }
	public string FromName { get; set; }
	public string FromAgentId { get; set; }
	public string ToDeveloperId { get; set; }
	public string ToAgentId { get; set; }
	public bool Broadcast { get; set; }
	public string Subject { get; set; }
	public string Body { get; set; }
	public long? TaskId { get; set; }
	public List<string> Paths { get; set; } = new();
	public DateTimeOffset? CreatedAt { get; set; }
	public DateTimeOffset? ReadAt { get; set; }
	public DateTimeOffset? AckedAt { get; set; }
}

public sealed class Commit
{
	public string Sha { get; set; }
	public string ShortSha { get; set; }
	public string Repo { get; set; }
	public string Branch { get; set; }
	public string Message { get; set; }
	public string AuthorName { get; set; }
	public string AuthorLogin { get; set; }
	public string DeveloperId { get; set; }
	public string Url { get; set; }
	public DateTimeOffset? At { get; set; }
	public long? TaskId { get; set; }

	public string Headline => (Message ?? "").Split( '\n' )[0];
	public string Short => !string.IsNullOrEmpty( ShortSha ) ? ShortSha : Sha?.Length > 7 ? Sha[..7] : Sha;
}

public sealed class TestRun
{
	public string Id { get; set; }
	public string ProjectId { get; set; }
	public string DeveloperId { get; set; }
	public string AgentId { get; set; }
	public string CommitSha { get; set; }
	public string Branch { get; set; }
	public string Build { get; set; }
	public string Scene { get; set; }
	public string Description { get; set; }
	public string Status { get; set; }
	public List<string> Errors { get; set; } = new();
	public DateTimeOffset? StartedAt { get; set; }
	public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class Blocker
{
	public string Kind { get; set; }
	public string Title { get; set; }
	public string Detail { get; set; }
	public DateTimeOffset? At { get; set; }
	public string RefId { get; set; }
}

public sealed class Change
{
	public string Id { get; set; }
	public string Status { get; set; }
	public string Summary { get; set; }
	public string DeveloperId { get; set; }
	public string DeveloperName { get; set; }
	public long? TaskId { get; set; }
	public string CommitSha { get; set; }
	public List<string> BreakingChanges { get; set; } = new();
	public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class TeamEntry
{
	public Developer Developer { get; set; }
	public List<Agent> Agents { get; set; } = new();
}

public sealed class Overview
{
	public CollabProject Project { get; set; }
	public Developer Me { get; set; }
	public List<TeamEntry> Team { get; set; } = new();
	public List<Reservation> Reservations { get; set; } = new();
	public List<TaskItem> TasksInProgress { get; set; } = new();
	public List<Blocker> Blockers { get; set; } = new();
	public List<Commit> RecentCommits { get; set; } = new();
	public List<Change> RecentChanges { get; set; } = new();
	public List<Activity> Activity { get; set; } = new();
	public int UnreadMessages { get; set; }
	public TestRun LastTest { get; set; }
}

public sealed class HeartbeatResult
{
	public Agent Agent { get; set; }
	public int UnreadMessages { get; set; }
	public List<string> Notices { get; set; } = new();
}

public sealed class BulkAssetResult
{
	public int Upserted { get; set; }
	public int Links { get; set; }
	public int Removed { get; set; }
}

/// <summary>One server-sent event: its type and the raw payload.</summary>
public sealed class ServerEvent
{
	public string Type { get; set; }
	public string ProjectId { get; set; }
	public DateTimeOffset? At { get; set; }
	public EventActor Actor { get; set; }
	public JsonElement Data { get; set; }
}

public sealed class EventActor
{
	public string DeveloperId { get; set; }
	public string AgentId { get; set; }
}
