using Godot;

[Tool]
[GlobalClass]
public partial class SideGuardGap : Resource
{
	public enum SideGuardGapSide
	{
		Left,
		Right,
		Both
	}

	[Export(PropertyHint.None, "suffix:m")]
	public float Position { get; set; } = 0f;
	[Export(PropertyHint.None, "suffix:m")]
	public float Width { get; set; } = 1f;
	[Export]
	public SideGuardGapSide Side { get; set; } = SideGuardGapSide.Left;

	public SideGuardGap() : this(0f, 1f) {}

	public SideGuardGap(float position, float width)
	{
		Position = position;
		Width = width;
	}
}
