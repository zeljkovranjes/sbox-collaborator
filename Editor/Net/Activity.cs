using System.Text.Json;

namespace Collaborator.EditorTools.Net;

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
