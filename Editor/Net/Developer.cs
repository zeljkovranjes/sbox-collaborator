using System.Text.Json;

namespace Collaborator.EditorTools.Net;

public sealed class Developer
{
	public string Id { get; set; }
	public string DisplayName { get; set; }
	public string GithubLogin { get; set; }
	public string Role { get; set; }
	public bool Online { get; set; }

	public string Name => string.IsNullOrEmpty( DisplayName ) ? Id : DisplayName;
}
