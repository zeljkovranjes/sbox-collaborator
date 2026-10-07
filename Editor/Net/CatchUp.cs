using System.Text.Json;

namespace Collaborator.EditorTools.Net;

/// <summary>What changed while you were away (team_catch_up).</summary>
public sealed class CatchUp
{
	public DateTimeOffset? Since { get; set; }
	public DateTimeOffset? Until { get; set; }
	public string Summary { get; set; }
	public CatchUpCounts Counts { get; set; } = new();
}
