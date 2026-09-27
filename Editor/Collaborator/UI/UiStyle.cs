namespace Collaborator.UI;

/// <summary>
/// Shared look of the Collaborator dock: the same language as the weapon importer (rounded
/// dark-gray cards on the window gray, green accent, uppercase hairline headers, status dots).
/// </summary>
public static class UiStyle
{
	/// <summary>One height for every control in a row: inputs, buttons, click fields.</summary>
	public const float ControlHeight = 26f;
	public const float RowSpacing = 6f;
	public const float Radius = 4f;
	public const float LabelWidth = 76f;

	public static Color ButtonFill => Color.Lerp( Theme.ControlBackground.WithAlpha( 1f ), Color.White, .07f );
	public static Color ButtonEdge => Color.Lerp( Theme.ControlBackground.WithAlpha( 1f ), Color.White, .15f );
	public static Color InputEdge => Color.Lerp( Theme.ControlBackground.WithAlpha( 1f ), Color.White, .17f );
	public static Color CardEdge => Theme.ControlBackground.Lighten( .25f );

	/// <summary>Lets a wrapping label break long paths after separators.</summary>
	public static string Breakable( string text ) => string.IsNullOrEmpty( text ) ? text
		: System.Text.RegularExpressions.Regex.Replace( text, "([_./\\\\-])", "$1​" );

	/// <summary>Inputs sit on the cards' dark gray; a darker fill and a thin edge keep them visible as inputs.</summary>
	public static T Framed<T>( T input, float height = ControlHeight ) where T : Widget
	{
		input.SetStyles( $"background-color: {Theme.WindowBackground.Hex}; border: 1px solid {InputEdge.Hex}; border-radius: {Radius}px; padding-left: 6px;" );
		if ( height > 0 )
			input.FixedHeight = height;
		return input;
	}

	public static Label Muted( Label label, bool small = false )
	{
		label.SetStyles( small ? $"color: {Theme.TextLight.Hex}; font-size: 11px;" : $"color: {Theme.TextLight.Hex};" );
		return label;
	}

	public static Label Bold( Label label, float size = 0 )
	{
		label.SetStyles( size > 0 ? $"font-weight: 600; font-size: {size}px;" : "font-weight: 600;" );
		return label;
	}

	public static Label Colored( Label label, Color color, bool bold = false )
	{
		label.SetStyles( $"color: {color.Hex};" + (bold ? " font-weight: 600;" : "") );
		return label;
	}

	public static Label Mono( Label label, Color? color = null )
	{
		label.SetStyles( $"font-family: Consolas, monospace; color: {(color ?? Theme.TextLight).Hex};" );
		return label;
	}

	public static Button Primary( string text, string icon, Action clicked, string tooltip = null, float height = 30 )
		=> new Button.Primary( text ) { Icon = icon, Tint = Theme.Green, FixedHeight = height, Clicked = clicked, ToolTip = tooltip };

	public static UiButton Secondary( Widget parent, string text, string icon, Action clicked, string tooltip = null, float height = ControlHeight )
		=> new( parent, text, icon, clicked, tooltip, height );

	/// <summary>Square icon button in the secondary style.</summary>
	public static IconButton Icon( Widget parent, string icon, Action clicked, string tooltip, float size = ControlHeight )
		=> new IconButton( icon, clicked, parent )
		{
			FixedSize = size,
			IconSize = 16,
			ToolTip = tooltip,
			Background = ButtonFill,
			Foreground = Theme.Text,
			BackgroundActive = Theme.Green.WithAlpha( .2f ),
			ForegroundActive = Theme.Green,
		};

	/// <summary>Agent/presence status colours, shared by pills and dots.</summary>
	public static Color StatusColor( string status ) => status switch
	{
		"working" => Theme.Green,
		"testing" => Theme.Blue,
		"planning" => Theme.Blue.Lighten( .3f ),
		"reviewing" => Theme.Pink,
		"blocked" => Theme.Red,
		"idle" => Theme.Yellow,
		_ => Theme.TextLight,
	};

	public static Color TaskColor( string status ) => status switch
	{
		"in_progress" => Theme.Green,
		"claimed" => Theme.Blue,
		"blocked" => Theme.Red,
		"review" => Theme.Pink,
		"available" => Theme.Yellow,
		"done" => Theme.TextLight,
		_ => Theme.TextLight,
	};

	public static Color PriorityColor( string priority ) => priority switch
	{
		"urgent" => Theme.Red,
		"high" => Theme.Yellow,
		"low" => Theme.TextLight.Darken( .2f ),
		_ => Theme.Blue,
	};

	public static string ClientIcon( string clientType ) => clientType switch
	{
		"sbox-editor" => "videogame_asset",
		"claude-code" => "smart_toy",
		"codex" => "terminal",
		"cursor" => "code",
		"opencode" => "data_object",
		_ => "memory",
	};

	/// <summary>A stable colour per person for avatars.</summary>
	public static Color PersonColor( string id )
	{
		Color[] palette = { Theme.Green, Theme.Blue, Theme.Yellow, Theme.Pink, new Color( 0.55f, 0.8f, 0.95f ), new Color( 1f, 0.6f, 0.2f ) };
		var hash = 0;
		foreach ( var c in id ?? "" )
			hash = hash * 31 + c;
		return palette[Math.Abs( hash ) % palette.Length];
	}

	public static string Ago( DateTimeOffset? time )
	{
		if ( time is not { } t )
			return "";
		var span = DateTimeOffset.UtcNow - t;
		if ( span.TotalSeconds < 45 )
			return "just now";
		if ( span.TotalMinutes < 60 )
			return $"{(int)span.TotalMinutes}m ago";
		if ( span.TotalHours < 24 )
			return $"{(int)span.TotalHours}h ago";
		if ( span.TotalDays < 7 )
			return $"{(int)span.TotalDays}d ago";
		return t.ToLocalTime().ToString( "d MMM" );
	}

	public static string Until( DateTimeOffset? time )
	{
		if ( time is not { } t )
			return "";
		var span = t - DateTimeOffset.UtcNow;
		if ( span.TotalSeconds <= 0 )
			return "expiring";
		if ( span.TotalMinutes < 60 )
			return $"{Math.Max( 1, (int)span.TotalMinutes )}m left";
		return $"{(int)span.TotalHours}h {(int)(span.TotalMinutes % 60)}m left";
	}

	public static string Clock( DateTimeOffset? time ) => time?.ToLocalTime().ToString( "HH:mm" ) ?? "";
}
