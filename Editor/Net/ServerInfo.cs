using System.Text.Json;

namespace Collaborator.EditorTools.Net;

// Shapes of the server's entities (docs/http-api.md). Only the fields the editor shows are
// declared; unknown fields are ignored. Timestamps stay nullable so a missing one never fails a read.

public sealed class ServerInfo
{
	public string Name { get; set; }
	public string Version { get; set; }
	public bool GithubLogin { get; set; }
	public bool KeyLogin { get; set; }
}
