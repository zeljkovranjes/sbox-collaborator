namespace Collaborator.EditorTools.UI.Widgets;

/// <summary>
/// The dock's page tabs: a horizontal strip with icon + name, a green underline on the active tab
/// and count badges. When the dock is too narrow for names, tabs collapse to icons (names move to
/// tooltips). Tabs flagged as trailing (settings) sit at the right end.
/// </summary>
public sealed class TabStrip : Widget
{
	private sealed class Tab
	{
		public string Icon;
		public string Title;
		public string Tip;
		public bool Trailing;
		public int Badge;
		public Color BadgeColor;
		public Rect Rect;
	}

	private readonly List<Tab> _tabs = new();
	private int _hover = -1;
	private bool _compact;

	public int Selected { get; private set; }
	public Action<int> OnSelected { get; set; }

	public TabStrip( Widget parent ) : base( parent )
	{
		FixedHeight = 36;
		MouseTracking = true;
		Cursor = CursorShape.Finger;
	}

	public void Add( string icon, string title, string tip, bool trailing = false )
	{
		_tabs.Add( new Tab { Icon = icon, Title = title, Tip = tip, Trailing = trailing } );
		Update();
	}

	public void SetBadge( int tab, int count, Color color )
	{
		var t = _tabs[tab];
		if ( t.Badge == count && t.BadgeColor == color )
			return;
		t.Badge = count;
		t.BadgeColor = color;
		Update();
	}

	public void Select( int tab, bool notify = true )
	{
		Selected = Math.Clamp( tab, 0, Math.Max( 0, _tabs.Count - 1 ) );
		Update();
		if ( notify )
			OnSelected?.Invoke( Selected );
	}

	private static string BadgeText( int n ) => n > 99 ? "99+" : n.ToString();

	/// <summary>Width of a tab with its name (icon, gap, text, badge, padding).</summary>
	private static float WideWidth( Tab t )
	{
		Paint.SetDefaultFont( 9, 600 );
		var w = 12 + 16 + 6 + Paint.MeasureText( t.Title ).x + 12;
		if ( t.Badge > 0 )
			w += 6 + MathF.Max( 16, 7 * BadgeText( t.Badge ).Length + 8 );
		return MathF.Ceiling( w );
	}

	private void LayoutTabs()
	{
		var leading = _tabs.Where( t => !t.Trailing ).ToList();
		var trailing = _tabs.Where( t => t.Trailing ).ToList();
		const float iconOnly = 40;
		var wide = leading.Sum( WideWidth ) + trailing.Count * iconOnly;
		_compact = wide > Width;

		var x = 0f;
		foreach ( var t in leading )
		{
			var w = _compact ? iconOnly : WideWidth( t );
			t.Rect = new Rect( x, 0, w, Height );
			x += w;
		}
		var right = Width;
		foreach ( var t in trailing.AsEnumerable().Reverse() )
		{
			right -= iconOnly;
			t.Rect = new Rect( right, 0, iconOnly, Height );
		}
	}

	private int TabAt( Vector2 p )
	{
		for ( var i = 0; i < _tabs.Count; i++ )
			if ( _tabs[i].Rect.IsInside( p ) )
				return i;
		return -1;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		var tab = TabAt( e.LocalPosition );
		if ( tab == _hover )
			return;
		_hover = tab;
		ToolTip = tab < 0 ? null : _compact || _tabs[tab].Trailing ? $"{_tabs[tab].Title}: {_tabs[tab].Tip}" : _tabs[tab].Tip;
		Update();
	}

	protected override void OnMouseLeave()
	{
		_hover = -1;
		Update();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton )
			return;
		var tab = TabAt( e.LocalPosition );
		if ( tab >= 0 )
			Select( tab );
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		LayoutTabs();

		// Hairline along the bottom; the active tab's underline sits on it.
		Paint.SetPen( Theme.ControlBackground.Lighten( .45f ), 1 );
		Paint.DrawLine( new Vector2( 0, Height - .5f ), new Vector2( Width, Height - .5f ) );

		for ( var i = 0; i < _tabs.Count; i++ )
		{
			var t = _tabs[i];
			var r = t.Rect;
			var active = i == Selected;
			var hover = i == _hover;
			if ( hover && !active )
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.ControlBackground.Lighten( .2f ) );
				Paint.DrawRect( new Rect( r.Left + 2, r.Top + 3, r.Width - 4, r.Height - 8 ), 4 );
			}
			var color = active ? Theme.Text : hover ? Theme.Text.WithAlpha( .85f ) : Theme.TextLight;
			var iconOnly = _compact || t.Trailing;
			var x = iconOnly ? r.Left + (r.Width - 16) * .5f : r.Left + 12;
			Paint.SetPen( active ? Theme.Green : color );
			Paint.DrawIcon( new Rect( x, r.Top, 16, r.Height - 3 ), t.Icon, 16 );
			x += 16 + 6;
			if ( !iconOnly )
			{
				Paint.SetDefaultFont( 9, active ? 600 : 500 );
				Paint.SetPen( color );
				var textWidth = Paint.MeasureText( t.Title ).x;
				Paint.DrawText( new Rect( x, r.Top, textWidth + 2, r.Height - 3 ), t.Title, TextFlag.LeftCenter );
				x += textWidth + 6;
			}

			if ( t.Badge > 0 )
			{
				if ( iconOnly )
				{
					Paint.ClearPen();
					Paint.SetBrush( t.BadgeColor );
					Paint.DrawRect( new Rect( r.Right - 13, r.Top + 7, 7, 7 ), 3.5f );
				}
				else
				{
					var text = BadgeText( t.Badge );
					var w = MathF.Max( 16, 7 * text.Length + 8 );
					var badge = new Rect( x, r.Center.y - 9.5f, w, 15 );
					Paint.ClearPen();
					Paint.SetBrush( t.BadgeColor.WithAlpha( .22f ) );
					Paint.DrawRect( badge, 7.5f );
					Paint.SetDefaultFont( 7, 700 );
					Paint.SetPen( t.BadgeColor );
					Paint.DrawText( badge, text, TextFlag.Center );
				}
			}

			if ( active )
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.Green );
				Paint.DrawRect( new Rect( r.Left + 6, Height - 3, r.Width - 12, 3 ), 1.5f );
			}
		}
	}
}
