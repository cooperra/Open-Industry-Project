using Godot;
using System.Collections.Generic;

[Tool]
public partial class ConveyorAssembly : Node3D
{
	#region Constants
	private const string AUTO_LEG_STAND_NAME_PREFIX = "AutoLegsStand";
	private const string AUTO_LEG_STAND_NAME_FRONT = "AutoLegsStandFront";
	private const string AUTO_LEG_STAND_NAME_REAR = "AutoLegsStandRear";
	private const int LEG_INDEX_FRONT = -1;
	private const int LEG_INDEX_REAR = -2;
	private const int LEG_INDEX_NON_AUTO = -3;
	#endregion Constants

	#region Fields
	#region Fields / Nodes
	protected Node3D conveyors;
	private Node3D rightSide;
	private Node3D leftSide;
	protected Node3D legStands;
	#endregion Fields / Nodes
	protected Transform3D transformPrev;
	private Transform3D conveyorsTransformPrev;
	private Transform3D legStandsTransformPrev;

	#region Fields / Exported properties
	[ExportGroup("Conveyor", "Conveyor")]
	[Export(PropertyHint.None, "radians_as_degrees")]
	public float ConveyorAngle { get; set; } = 0f;
	private float conveyorAnglePrev = 0f;

	[Export]
	public bool ConveyorAutoScale { get; set; } = true;

	[ExportGroup("Side Guards", "SideGuards")]
	[Export]
	public bool SideGuardsAutoScale { get; set; } = true;
	[Export]
	public bool SideGuardsLeftSide { get; set; } = true;
	[Export]
	public bool SideGuardsRightSide { get; set; } = true;
	[Export]
	public Godot.Collections.Array<SideGuardGap> SideGuardsGaps = new();

	[ExportGroup("Leg Stands", "AutoLegStands")]
	[Export(PropertyHint.None, "suffix:m")]
	public float AutoLegStandsFloorOffset = 0f;
	public float autoLegStandsFloorOffsetPrev;

	[ExportSubgroup("Interval Legs", "AutoLegStandsIntervalLegs")]
	[Export]
	public bool AutoLegStandsIntervalLegsEnabled { get; set; } = true;
	private bool autoLegStandsIntervalLegsEnabledPrev = false;

	[Export(PropertyHint.Range, "0.5,10,or_greater,suffix:m")]
	public float AutoLegStandsIntervalLegsInterval { get; set; } = 2f;
	private float autoLegStandsIntervalLegsIntervalPrev;

	[Export(PropertyHint.Range, "-5,5,or_less,or_greater,suffix:m")]
	public float AutoLegStandsIntervalLegsOffset { get; set; } = 0f;
	private float autoLegStandsIntervalLegsOffsetPrev;

	[ExportSubgroup("End Legs", "AutoLegStandsEndLeg")]
	[Export]
	public bool AutoLegStandsEndLegFront = true;
	private bool autoLegStandsEndLegFrontPrev = false;
	[Export]
	public bool AutoLegStandsEndLegRear = true;
	private bool autoLegStandsEndLegRearPrev = false;

	[ExportSubgroup("Placement Margins", "AutoLegStandsMargin")]
	[Export(PropertyHint.Range, "0,1,or_less,or_greater,suffix:m")]
	public float AutoLegStandsMarginEnds = 0.2f;
	[Export(PropertyHint.Range, "0.5,5,or_greater,suffix:m")]
	public float AutoLegStandsMarginEndLegs = 0.5f;
	private float autoLegStandsMarginEndLegsPrev = 0.5f;

	[ExportSubgroup("Leg Model", "AutoLegStandsModel")]
	[Export(PropertyHint.None, "suffix:m")]
	public float AutoLegStandsModelGrabsOffset = 0.382f;

	[Export]
	public PackedScene AutoLegStandsModelScene = GD.Load<PackedScene>("res://parts/ConveyorLegBC.tscn");
	private PackedScene autoLegStandsModelScenePrev;
	#endregion Fields / Exported properties

	#region Fields / Leg stand coverage
	private float legStandCoverageMin;
	private float legStandCoverageMax;
	private float legStandCoverageMinPrev;
	private float legStandCoverageMaxPrev;
	#endregion Fields / Leg stand coverage

	// This variable is used to store the names of the pre-existing leg stands that can't be owned by the edited scene.
	private Dictionary <StringName, Node> foreignLegStandsOwners = new();
	#endregion Fields

	#region _Ready and _PhysicsProcess
	public override void _Ready()
	{
		conveyors = GetNode<Node3D>("Conveyors");
		rightSide = GetNodeOrNull<Node3D>("RightSide");
		leftSide = GetNodeOrNull<Node3D>("LeftSide");
		legStands = GetNodeOrNull<Node3D>("LegStands");

		transformPrev = this.Transform;

		// Apply the ConveyorsAngle property if needed.
		Basis assemblyScale = Basis.Identity.Scaled(this.Basis.Scale);
		if (conveyors != null) {
			float conveyorsStartingAngle = (assemblyScale * conveyors.Basis).GetEuler().Z;
			conveyorAnglePrev = conveyorsStartingAngle;
			conveyorsTransformPrev = conveyors.Transform;
			SyncConveyorsAngle();
		}

		// Apply the AutoLegStandsFloorOffset and AutoLegStandsIntervalLegsOffset properties if needed.
		if (legStands != null) {
			Vector3 legStandsStartingOffset = assemblyScale * legStands.Position;
			autoLegStandsFloorOffsetPrev = legStandsStartingOffset.Y;
			autoLegStandsIntervalLegsOffsetPrev = legStandsStartingOffset.X;
			legStandsTransformPrev = legStands.Transform;
			SyncLegStandsOffsets();
		}

		autoLegStandsIntervalLegsIntervalPrev = AutoLegStandsIntervalLegsInterval;
		autoLegStandsModelScenePrev = AutoLegStandsModelScene;
		UpdateLegStandCoverage();

		if (legStands != null) {
			Node editedScene = GetTree().GetEditedSceneRoot();
			foreach (Node legStand in legStands.GetChildren()) {
				if (legStand.Owner != editedScene) {
					foreignLegStandsOwners[legStand.Name] = legStand.Owner;
				}
			}
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		ApplyAssemblyScaleConstraints();
		PreventAllChildScaling();
		UpdateConveyors();
		UpdateSides();
		UpdateLegStandCoverage();
		UpdateLegStands();
		transformPrev = this.Transform;
		autoLegStandsIntervalLegsEnabledPrev = AutoLegStandsIntervalLegsEnabled;
		autoLegStandsEndLegFrontPrev = AutoLegStandsEndLegFront;
		autoLegStandsEndLegRearPrev = AutoLegStandsEndLegRear;
		autoLegStandsMarginEndLegsPrev = AutoLegStandsMarginEndLegs;
		autoLegStandsModelScenePrev = AutoLegStandsModelScene;
	}

	protected virtual void ApplyAssemblyScaleConstraints()
	{
		// There are no constraints for this assembly.
		// This is where one would lock scale components equal to each other or a constant value, for example.
	}
	#endregion _Ready and _PhysicsProcess

	#region Decouple assembly scale from child scale
	private void PreventAllChildScaling() {
		foreach (Node3D child in GetChildren()) {
			Node3D child3D = child as Node3D;
			if (child3D != null) {
				PreventChildScaling(child3D);
			}
		}
	}

	/**
	 * Counteract the scaling of child nodes as the parent node scales.
	 *
	 * This is a hack to allow us to decouple the scaling of the assembly from the scaling of its parts.
	 *
	 * Child nodes will appear not to scale, but actually, scale inversely to the parent.
	 * Parent scale will still affect the child's position, but not its apparent rotation.
	 *
	 * The downside is the child's scale will be appear locked to (1, 1, 1).
	 * This is why all of our scalable parts aren't direct children of the assembly.
	 *
	 * @param child The child node to prevent scaling.
	 */
	private void PreventChildScaling(Node3D child) {
		var basisRotation = this.Transform.Basis.Orthonormalized();
		var basisScale = basisRotation.Inverse() * this.Transform.Basis;
		var xformScaleInverse = new Transform3D(basisScale, new Vector3(0, 0, 0)).AffineInverse();

		var basisRotationPrev = transformPrev.Basis.Orthonormalized();
		var basisScalePrev = basisRotationPrev.Inverse() * transformPrev.Basis;
		var xformScalePrev = new Transform3D(basisScalePrev, new Vector3(0, 0, 0));

		// The child transform without the effects of the parent's scale.
		var childTransformUnscaled = xformScalePrev * child.Transform;

		// Remove any remaining scale. This effectively locks child's scale to (1, 1, 1).
		childTransformUnscaled.Basis = childTransformUnscaled.Basis.Orthonormalized();

		// Adjust child's position with changes in the parent's scale.
		childTransformUnscaled.Origin *= basisScalePrev.Inverse() * basisScale;

		// Reapply inverse parent scaling to child.
		child.Transform = xformScaleInverse * childTransformUnscaled;
	}
	#endregion Decouple assembly scale from child scale
}
