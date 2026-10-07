using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class AccessKey
{
	public string Id { get; set; }
	public string Name { get; set; }
	public string Prefix { get; set; }
	public List<string> Scopes { get; set; } = new();
}
