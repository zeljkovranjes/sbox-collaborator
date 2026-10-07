using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class Me
{
	public Developer Developer { get; set; }
	public AccessKey Key { get; set; }
	public List<string> Scopes { get; set; } = new();
	public List<string> ProjectIds { get; set; }

	public bool CanWrite => Scopes.Contains( "write" ) || Scopes.Contains( "admin" );
}
