using System.Text.Json;

namespace Collaborator.EditorTools.Net;

/// <summary>One server-sent event: its type and the raw payload.</summary>
public sealed class ServerEvent
{
	public string Type { get; set; }
	public string ProjectId { get; set; }
	public DateTimeOffset? At { get; set; }
	public EventActor Actor { get; set; }
	public JsonElement Data { get; set; }
}
