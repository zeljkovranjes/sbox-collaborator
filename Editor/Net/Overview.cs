using System.Text.Json;

namespace Collaborator.EditorTools.Net;

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
