using Godot;
using System;

[Tool]
public partial class ConveyorAssembly : Node3D
{
	private const int ROUNDING_DIGITS = 3;
	protected Node3D conveyors;
	private Node3D rightSide;
	private Node3D leftSide;
	protected Node3D legStands;
	protected Transform3D transformPrev;

	[ExportGroup("Conveyor Automation", "Auto")]
	[Export]
	public bool AutoScaleConveyors { get; set; } = true;
	[Export]
	public bool AutoScaleGuards { get; set; } = true;

	[ExportGroup("Leg Stands Automation", "AutoLegStands")]
	[Export]
	public bool AutoLegStandsUseInterval { get; set; } = true;
	private bool autoLegStandsUseIntervalPrev = false;

	[Export(PropertyHint.Range, "0.5,10,or_greater,suffix:m")] // TODO use _ValidateProperty to override this for the CurvedConveyorAssembly degrees unit
	public float AutoLegStandsInterval { get; set; } = 2f;
	private float autoLegStandsIntervalPrev;
	[Export(PropertyHint.Range, "0,1,or_less,or_greater,suffix:m")]
	public float AutoLegStandsEndOffset = 0.2f;

	[ExportSubgroup("Fixed Legs", "AutoLegStandsFixed")]
	[Export]
	public bool AutoLegStandsFixedFrontLeg = true;
	private bool autoLegStandsFixedFrontLegPrev = false;
	[Export]
	public bool AutoLegStandsFixedRearLeg = true;
	private bool autoLegStandsFixedRearLegPrev = false;
	[Export(PropertyHint.Range, "0.5,5,or_greater,suffix:m")]
	public float AutoLegStandsFixedLegMargin = 0.5f;
	private float autoLegStandsFixedLegMarginPrev = 0.5f;

	[ExportSubgroup("Leg Model", "AutoLegStandsModel")]
	[Export(PropertyHint.None, "suffix:m")]
	public float AutoLegStandsModelGrabsOffset = 0.382f;

	[Export]
	public PackedScene AutoLegStandsModelScene = GD.Load<PackedScene>("res://parts/ConveyorLegBC.tscn");
	private PackedScene autoLegStandsModelScenePrev;

	private float legStandCoverageMin;
	private float legStandCoverageMax;
	private float legStandCoverageMinPrev;
	private float legStandCoverageMaxPrev;

	public override void _Ready()
	{
		conveyors = GetNode<Node3D>("Conveyors");
		rightSide = GetNodeOrNull<Node3D>("RightSide");
		leftSide = GetNodeOrNull<Node3D>("LeftSide");
		legStands = GetNodeOrNull<Node3D>("LegStands");
		transformPrev = this.Transform;
		autoLegStandsIntervalPrev = AutoLegStandsInterval;
		autoLegStandsModelScenePrev = AutoLegStandsModelScene;
		UpdateLegStandCoverage();
		// All existing leg stands that we own must be regenerated. We can't trust them remain correct.
		// ^ The above claim might not be true now that we SetEditableInstance(legStand, true) on all our leg stands.
		// Still, deleting all the leg stands is safer until we're certain that there won't be issues.
		DeleteSelfOwnedLegStands();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (GetChildCount() == 0){
			SetupDefaultChildren();
		}
		ApplyAssemblyScaleConstraints();
		PreventAllChildScaling();
		UpdateConveyors();
		UpdateSides();
		UpdateLegStandCoverage();
		UpdateLegStands();
		transformPrev = this.Transform;
		autoLegStandsUseIntervalPrev = AutoLegStandsUseInterval;
		autoLegStandsFixedFrontLegPrev = AutoLegStandsFixedFrontLeg;
		autoLegStandsFixedRearLegPrev = AutoLegStandsFixedRearLeg;
		autoLegStandsFixedLegMarginPrev = AutoLegStandsFixedLegMargin;
		autoLegStandsModelScenePrev = AutoLegStandsModelScene;
	}

	private void SetupDefaultChildren()
	{

		GD.Print("Creating default children.");
		var root = GetTree().EditedSceneRoot;
		if (root == null)
		{
			GD.Print("No root node found.");
			return;
		}

		conveyors = new Node3D();
		conveyors.Name = "Conveyors";
		AddChild(conveyors);
		conveyors.Owner = root;

		rightSide = new Node3D();
		rightSide.Name = "RightSide";
		AddChild(rightSide);
		conveyors.Owner = root;

		leftSide = new Node3D();
		leftSide.Name = "LeftSide";
		AddChild(leftSide);
		conveyors.Owner = root;

		legStands = new Node3D();
		legStands.Name = "LegStands";
		AddChild(legStands);
		conveyors.Owner = root;
		GD.Print("Default children created.");
	}

	protected virtual void ApplyAssemblyScaleConstraints()
	{
		// There are no constraints for this assembly.
		// This is where one would lock scale components equal to each other or a constant value, for example.
	}

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
	 * This is a hack to allow us to handle grandchildren scale manually.
	 *
	 * Child nodes will appear not to scale, but actually, scale inversely to the parent.
	 * Parent scale will still affect the child's position, but not its apparent rotation.
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

	private void UpdateConveyors()
	{
		if (conveyors == null)
		{
			return;
		}

		LockConveyorsGroup();

		var conveyorLineLength = GetConveyorLineLength();
		// TODO enact a scheme where conveyors are scaled proportionally to the each other. (But there is that factor where the tips will overlap... Better than nothing!)

		foreach (Node child in conveyors.GetChildren()) {
			Node3D child3d = child as Node3D;
			if (IsConveyor(child3d)) {
				SetEditableInstance(child3d, true);
				ScaleConveyor(child3d, conveyorLineLength);
			}
			if (IsSideGuard(child3d)) {
				SetEditableInstance(child3d, true);
				ScaleSideGuard(child3d, conveyorLineLength);
			}
		}
	}

	protected virtual void LockConveyorsGroup() {
		// Lock Z position
		conveyors.Position = new Vector3(conveyors.Position.X, conveyors.Position.Y, 0f);
		// Lock X and Y rotation
		if (conveyors.Rotation.X > 0.001f || conveyors.Rotation.X < -0.001f || conveyors.Rotation.Y > 0.001f || conveyors.Rotation.Y < -0.001) {
			// This seems to mess up scale, but at least that's fixed on the next frame.
			conveyors.Rotation = new Vector3(0f, 0f, conveyors.Rotation.Z);
		}
	}

	private float GetConveyorLineLength() {
		if (conveyors == null) {
			return this.Scale.X;
		}
		if (AutoScaleConveyors) {
			var cos = Mathf.Cos(conveyors.Basis.GetEuler().Z);
			return this.Scale.X * 1 / (Mathf.Abs(cos) >= 0.01f ? cos : 0.01f);
		}
		// Add up the length of all conveyors.
		// Assume all conveyors are aligned end-to-end.
		var sum = 0f;
		foreach (Node child in conveyors.GetChildren()) {
			Node3D conveyor = child as Node3D;
			if (IsConveyor(conveyor)) {
				// Assume conveyor scale == length.
				sum += conveyor.Scale.X;
			}
		}
		return sum;
	}

	private static bool IsConveyor(Node node) {
		return node as IConveyor != null || node as RollerConveyor != null || node as CurvedRollerConveyor != null;
	}

	private bool IsSideGuard(Node node) {
		return node as SideGuard != null || node as SideGuardCBC != null;
	}

	protected virtual void ScaleConveyor(Node3D conveyor, float conveyorLineLength) {
		if (AutoScaleConveyors) {
			conveyor.Scale = new Vector3(conveyorLineLength, 1f, this.Scale.Z);
		} else {
			// Always scale width.
			conveyor.Scale = new Vector3(conveyor.Scale.X, conveyor.Scale.Y, this.Scale.Z);
		}
	}

	protected virtual void ScaleSideGuard(Node3D guard, float conveyorLineLength) {
		if (AutoScaleGuards) {
			guard.Scale = new Vector3(conveyorLineLength, 1f, 1f);
		}
	}

	private void UpdateSides()
	{
		UpdateSide(rightSide, true);
		UpdateSide(leftSide, false);
	}

	private void UpdateSide(Node3D side, bool isRight) {
		if (side == null || conveyors == null)
		{
			return;
		}

		// Sides always snap onto the conveyor line
		side.Transform = conveyors.Transform;
		var offsetZ = (isRight? -1 : 1) * side.Basis.Z * (this.Scale.Z - 1f);
		side.Position += offsetZ;

		if (!AutoScaleGuards) {
			return;
		}

		var conveyorLineLength = GetConveyorLineLength();

		foreach (Node child in side.GetChildren()) {
			Node3D guard = child as Node3D;
			if (guard == null) {
				continue;
			}
			// TODO scale x position of all items here
			// TODO proportional scaling of the guards
			if (IsSideGuard(guard)) {
				SetEditableInstance(guard, true);
				ScaleSideGuard(guard, conveyorLineLength);
			}
		}
	}

	private void UpdateLegStandCoverage() {
		(legStandCoverageMinPrev, legStandCoverageMaxPrev) = (legStandCoverageMin, legStandCoverageMax);
		(legStandCoverageMin, legStandCoverageMax) = GetLegStandCoverage();
	}

	protected virtual (float, float) GetLegStandCoverage() {
		if (legStands == null || conveyors == null) {
			return (0f, 0f);
		}
		var min = float.MaxValue;
		var max = float.MinValue;
		foreach (Node child in conveyors.GetChildren()) {
			Node3D conveyor = child as Node3D;
			if (IsConveyor(conveyor)) {
				// Get the conveyor's Transform in the legStands space.
				Transform3D localConveyorTransform = legStands.Transform.AffineInverse() * conveyors.Transform * conveyor.Transform;
				// Get the X extents of the conveyor in the legStands space.
				Vector3 conveyorExtent1 = localConveyorTransform.Orthonormalized() * new Vector3(-Mathf.Abs(localConveyorTransform.Basis.Scale.X * 0.5f) + AutoLegStandsEndOffset, -AutoLegStandsModelGrabsOffset, 0f);
				Vector3 conveyorExtent2 = localConveyorTransform.Orthonormalized() * new Vector3(Mathf.Abs(localConveyorTransform.Basis.Scale.X * 0.5f) - AutoLegStandsEndOffset, -AutoLegStandsModelGrabsOffset, 0f);
				// Update min and max.
				min = Mathf.Min(min, Mathf.Min(conveyorExtent2.X, conveyorExtent1.X));
				max = Mathf.Max(max, Mathf.Max(conveyorExtent2.X, conveyorExtent1.X));
			}
		}
		// Round to avoid floating point errors.
		return ((float) Math.Round(min, ROUNDING_DIGITS), (float) Math.Round(max, ROUNDING_DIGITS));
	}

	private void UpdateLegStands()
	{
		if (legStands == null)
		{
			return;
		}

		LockLegStandsGroup();

		// If the leg stand scene changes, we need to regenerate everything.
		if (AutoLegStandsModelScene != autoLegStandsModelScenePrev) {
			DeleteSelfOwnedLegStands();
		}

		SnapAllLegStandsToPath();

		var autoLegStandsUpdateIsNeeded = AutoLegStandsUseInterval != autoLegStandsUseIntervalPrev
			|| AutoLegStandsInterval != autoLegStandsIntervalPrev
			|| AutoLegStandsFixedFrontLeg != autoLegStandsFixedFrontLegPrev
			|| AutoLegStandsFixedRearLeg != autoLegStandsFixedRearLegPrev
			|| AutoLegStandsFixedLegMargin != autoLegStandsFixedLegMarginPrev
			|| AutoLegStandsModelScene != autoLegStandsModelScenePrev
			|| legStandCoverageMin != legStandCoverageMinPrev
			|| legStandCoverageMax != legStandCoverageMaxPrev;
		if (autoLegStandsUpdateIsNeeded) {
			// GD.Print("Updating leg stands. Reason: ", AutoLegStandsUseInterval != autoLegStandsUseIntervalPrev
			// , AutoLegStandsInterval != autoLegStandsIntervalPrev
			// , AutoLegStandsFixedFrontLeg != autoLegStandsFixedFrontLegPrev
			// , AutoLegStandsFixedRearLeg != autoLegStandsFixedRearLegPrev
			// , AutoLegStandsFixedLegMargin != autoLegStandsFixedLegMarginPrev
			// , AutoLegStandsModelScene != autoLegStandsModelScenePrev
			// , legStandCoverageMin != legStandCoverageMinPrev
			// , legStandCoverageMax != legStandCoverageMaxPrev);
			AdjustAutoLegStandPositions();
			CreateAndRemoveAutoLegStands();
		}
		UpdateLegStandsHeightAndVisibility();
	}

	protected virtual void LockLegStandsGroup() {
		// Always align LegStands group with Conveyors group.
		if (conveyors != null) {
			legStands.Position = new Vector3(legStands.Position.X, legStands.Position.Y, conveyors.Position.Z);
			// TODO rotation is funky now
			legStands.Rotation = new Vector3(0f, conveyors.Rotation.Y, 0f);
		}
	}

	private void DeleteSelfOwnedLegStands() {
		if (legStands == null) {
			return;
		}
		foreach (Node child in legStands.GetChildren()) {
			ConveyorLeg legStand = child as ConveyorLeg;
			if (legStand == null) {
				continue;
			}
			if (legStand.Owner == this) {
				legStands.RemoveChild(legStand);
				legStand.QueueFree();
			}
		}
	}

	private void SnapAllLegStandsToPath() {
		// Force legStand alignment with LegStands group.
		float targetWidth = GetLegStandTargetWitdh();
		foreach (Node child in legStands.GetChildren()) {
			ConveyorLeg legStand = child as ConveyorLeg;
			if (legStand == null) {
				continue;
			}
			SnapToLegStandsPath(legStand);
			legStand.Scale = new Vector3(1f, legStand.Scale.Y, targetWidth);
		}

	}

	private float GetLegStandTargetWitdh() {
		Node3D firstConveyor = null;
		foreach (Node child in conveyors.GetChildren()) {
			Node3D conveyor = child as Node3D;
			if (IsConveyor(conveyor)) {
				firstConveyor = conveyor;
				break;
			}
		}
		// This is a hack to account for the fact that rolling conveyors are slightly wider than belt conveyors.
		if (firstConveyor is RollerConveyor || firstConveyor is CurvedRollerConveyor) {
			return this.Scale.Z * 1.055f;
		}
		return this.Scale.Z;
	}

	private void SnapToLegStandsPath(Node3D legStand) {
		MoveLegStandToPathPosition(legStand, GetPositionOnLegStandsPath(legStand.Position));
	}

	protected virtual float GetPositionOnLegStandsPath(Vector3 position) {
		return position.X;
	}

	protected virtual void MoveLegStandToPathPosition(Node3D legStand, float position) {
		legStand.Position = new Vector3(position, legStand.Position.Y, 0f);
		legStand.Rotation = new Vector3(0f, 0f, legStand.Rotation.Z);
	}

	private void AdjustAutoLegStandPositions() {
		// Don't allow tiny or negative intervals.
		AutoLegStandsInterval = Mathf.Max(0.5f, AutoLegStandsInterval);
		if (AutoLegStandsInterval == autoLegStandsIntervalPrev && legStandCoverageMax == legStandCoverageMaxPrev && legStandCoverageMin == legStandCoverageMinPrev) {
			return;
		}
		foreach (Node child in legStands.GetChildren()) {
			ConveyorLeg legStand = child as ConveyorLeg;
			if (legStand == null) {
				continue;
			}
			// Only adjust leg stands that we created.
			if (legStand.Owner != this) {
				continue;
			}
			// Handle front and rear legs first.
			if (AutoLegStandsFixedFrontLeg && legStand.Position.X == legStandCoverageMinPrev) {
				legStand.Position = new Vector3(legStandCoverageMin, legStand.Position.Y, 0f);
				continue;
			}
			if (AutoLegStandsFixedRearLeg && legStand.Position.X == legStandCoverageMaxPrev) {
				legStand.Position = new Vector3(legStandCoverageMax, legStand.Position.Y, 0f);
				continue;
			}
			// Update leg stand position to the new interval.
			int legStandIndex = (int) Mathf.Round(GetPositionOnLegStandsPath(legStand.Position) / autoLegStandsIntervalPrev);
			MoveLegStandToPathPosition(legStand, (float) Math.Round(legStandIndex * AutoLegStandsInterval, ROUNDING_DIGITS));
		}

		autoLegStandsIntervalPrev = AutoLegStandsInterval;
	}

	private void CreateAndRemoveAutoLegStands() {
		// Don't allow negative margins.
		AutoLegStandsFixedLegMargin = Mathf.Max(0f, AutoLegStandsFixedLegMargin);
		// Enforce a margin from fixed front and rear legs if they exist.
		float frontMargin = AutoLegStandsFixedFrontLeg ? AutoLegStandsFixedLegMargin : 0f;
		float rearMargin = AutoLegStandsFixedRearLeg ? AutoLegStandsFixedLegMargin : 0f;
		float firstPosition = (float) Math.Ceiling((legStandCoverageMin + frontMargin) / AutoLegStandsInterval) * AutoLegStandsInterval;
		float lastPosition = (float) Math.Floor((legStandCoverageMax - rearMargin) / AutoLegStandsInterval) * AutoLegStandsInterval;
		int intervalLegStandCount;
		if (firstPosition > lastPosition) {
			// Invalid range implies zero interval-aligned leg stands are needed.
			intervalLegStandCount = 0;
		} else {
			intervalLegStandCount = (int) ((lastPosition - firstPosition) / AutoLegStandsInterval) + 1;
		}
		// Inventory our existing leg stands and delete the ones we don't need.
		var hasFrontLeg = false;
		var hasRearLeg = false;
		ConveyorLeg[] legStandsInventory = new ConveyorLeg[intervalLegStandCount];
		foreach (Node child in legStands.GetChildren()) {
			ConveyorLeg legStand = child as ConveyorLeg;
			if (legStand == null) {
				continue;
			}
			// Only manage leg stands that we created.
			if (legStand.Owner != this) {
				continue;
			}
			// Ignore existing front and rear legs.
			var isFrontLeg = AutoLegStandsFixedFrontLeg && GetPositionOnLegStandsPath(legStand.Position) == legStandCoverageMin;
			var isRearLeg = AutoLegStandsFixedRearLeg && GetPositionOnLegStandsPath(legStand.Position) == legStandCoverageMax;
			if (isFrontLeg || isRearLeg) {
				hasFrontLeg = hasFrontLeg || isFrontLeg;
				hasRearLeg = hasRearLeg || isRearLeg;
				continue;
			}
			if (AutoLegStandsUseInterval) {
				// Delete leg stands that are not in the new interval.
				if (GetPositionOnLegStandsPath(legStand.Position) < firstPosition || GetPositionOnLegStandsPath(legStand.Position) > lastPosition) {
					legStand.QueueFree();
				} else {
					// Store leg stands that are in the new interval.
					legStandsInventory[(int) Math.Round((GetPositionOnLegStandsPath(legStand.Position) - firstPosition) / AutoLegStandsInterval)] = legStand;
				}
			} else {
				// Delete all interval leg stands if we're not using intervals.
				legStand.QueueFree();
			}
		}

		// Create the missing leg stands.
		if (AutoLegStandsModelScene == null) {
			return;
		}
		if (!hasFrontLeg && AutoLegStandsFixedFrontLeg) {
			ConveyorLeg legStand = AutoLegStandsModelScene.Instantiate() as ConveyorLeg;
			MoveLegStandToPathPosition(legStand, legStandCoverageMin);
			legStands.AddChild(legStand, forceReadableName: true);
			legStand.Owner = this;
		}
		if (!hasRearLeg && AutoLegStandsFixedRearLeg) {
			ConveyorLeg legStand = AutoLegStandsModelScene.Instantiate() as ConveyorLeg;
			MoveLegStandToPathPosition(legStand, legStandCoverageMax);
			legStands.AddChild(legStand, forceReadableName: true);
			legStand.Owner = this;
		}
		if (!AutoLegStandsUseInterval) {
			return;
		}
		for (int i = 0; i < intervalLegStandCount; i++) {
			if (legStandsInventory[i] == null) {
				ConveyorLeg legStand = AutoLegStandsModelScene.Instantiate() as ConveyorLeg;
				MoveLegStandToPathPosition(legStand, (float) Math.Round(firstPosition + i * AutoLegStandsInterval, ROUNDING_DIGITS));
				legStands.AddChild(legStand, forceReadableName: true);
				legStand.Owner = this;
			}
		}
	}

	private void UpdateLegStandsHeightAndVisibility() {
		// Extend LegStands to Conveyor line.
		if (conveyors == null)
		{
			return;
		}
		// Plane transformed from conveyors space into legStands space.
		Plane conveyorPlane = new Plane(Vector3.Up, new Vector3(0f, -AutoLegStandsModelGrabsOffset, 0f)) * conveyors.Transform.AffineInverse() * legStands.Transform;
		Vector3 conveyorPlaneGlobalNormal = conveyorPlane.Normal * legStands.GlobalBasis.Inverse();

		foreach (Node child in legStands.GetChildren()) {
			ConveyorLeg legStand = child as ConveyorLeg;
			if (legStand == null) {
				continue;
			}
			// Persist legStand changes into the Assembly's PackedScene.
			// Fixes ugly previews in the editor.
			SetEditableInstance(legStand, true);
			// Raycast from the minimum-height tip of the leg stand to the conveyor plane.
			Vector3? intersection = conveyorPlane.IntersectsRay(legStand.Position + legStand.Basis.Y.Normalized(), legStand.Basis.Y.Normalized());
			if (intersection == null) {
				legStand.Visible = false;
				// Set scale to minimum height.
				legStand.Scale = new Vector3(1f, 1f, legStand.Scale.Z);
				continue;
			}
			float legHeight = intersection.Value.DistanceTo(legStand.Position);
			legStand.Scale = new Vector3(1f, legHeight, legStand.Scale.Z);
			legStand.GrabsRotation = Mathf.RadToDeg(Vector3.Up.SignedAngleTo(conveyorPlaneGlobalNormal.Slide(legStand.GlobalBasis.Z), legStand.GlobalBasis.Z));
			// Only show leg stands that touch a conveyor.
			float tipPosition = GetPositionOnLegStandsPath(legStand.Position + legStand.Basis.Y);
			legStand.Visible = legStandCoverageMin <= tipPosition && tipPosition <= legStandCoverageMax;
		}
	}
}
