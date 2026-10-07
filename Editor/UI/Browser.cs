using Collaborator.EditorTools.Net;

namespace Collaborator.EditorTools.UI;

/// <summary>Opens links in the system browser.</summary>
public static class Browser
{
	public static void Open( string url )
	{
		if ( string.IsNullOrEmpty( url ) )
			return;
		try
		{
			System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo( url ) { UseShellExecute = true } );
		}
		catch ( Exception )
		{
			EditorUtility.OpenFolder( url );
		}
	}
}
