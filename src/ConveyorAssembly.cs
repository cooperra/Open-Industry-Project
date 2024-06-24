using Godot;
using System;

[Tool]
public partial class ConveyorAssembly : Node3D
{
	private const int ROUNDING_DIGITS = 3;
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
	[Export(PropertyHint.None, "radians_as_degrees")]
	public float ConveyorAngle { get; set; } = 0f;
	private float conveyorAnglePrev = 0f;

	[ExportGroup("Auto Scaling", "AutoScale")]
	[Export]
	public bool AutoScaleConveyors { get; set; } = true;
	[Export]
	public bool AutoScaleGuards { get; set; } = true;

	[ExportGroup("Auto Leg Stands", "AutoLegStands")]
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

		// Apply the AutoLegStandsIntervalLegsOffset property if needed.
		if (legStands != null) {
			float legStandsStartingOffset = (assemblyScale * legStands.Position).X;
			autoLegStandsIntervalLegsOffsetPrev = legStandsStartingOffset;
			legStandsTransformPrev = legStands.Transform;
			SyncLegStandsOffset();
		}

		autoLegStandsIntervalLegsIntervalPrev = AutoLegStandsIntervalLegsInterval;
		autoLegStandsModelScenePrev = AutoLegStandsModelScene;
		UpdateLegStandCoverage();

		// All existing leg stands that we own must be regenerated. We can't trust them remain correct.
		// ^ The above claim might not be true now that we SetEditableInstance(legStand, true) on all our leg stands.
		// Still, deleting all the leg stands is safer until we're certain that there won't be issues.
		DeleteSelfOwnedLegStands();
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

	#region Scaling Conveyors and Guards
	#region Scaling Conveyors and Guards / Update "Conveyors" node
	private void UpdateConveyors()
	{
		if (conveyors == null)
		{
			return;
		}

		LockConveyorsGroup();
		SyncConveyorsAngle();
		var conveyorLineLength = GetConveyorLineLength();
		ScaleConveyorLine(conveyors, conveyorLineLength);
		ScaleSideGuardLine(conveyors, conveyorLineLength);
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

	/**
	 * Synchronize the angle of the conveyors with the assembly's ConveyorAngle property.
	 *
	 * If the property changes, the conveyors are rotated to match.
	 * If the conveyors are rotated manually, the property is updated.
	 * If both happen at the same time, the property wins.
	 */
	private void SyncConveyorsAngle() {
		Basis scale = Basis.Identity.Scaled(this.Basis.Scale);
		Basis scalePrev = Basis.Identity.Scaled(transformPrev.Basis.Scale);
		if (ConveyorAngle != conveyorAnglePrev) {
			Basis targetRot = new Basis(new Vector3(0, 0, 1), ConveyorAngle);
			conveyors.Basis = scale.Inverse() * targetRot;
		} else {
			float angle = (scale * conveyors.Basis).GetEuler().Z;
			float anglePrev = (scalePrev * conveyorsTransformPrev.Basis).GetEuler().Z;
			double angleDelta = Mathf.Abs(angle - anglePrev) % (2 * Math.PI);
			if (angleDelta > Math.PI / 360.0) {
				this.ConveyorAngle = (scale * conveyors.Basis).GetEuler().Z;
				NotifyPropertyListChanged();
			}
		}
		conveyorAnglePrev = ConveyorAngle;
		conveyorsTransformPrev = conveyors.Transform;
	}
	#endregion Scaling Conveyors and Guards / Update "Conveyors" node

	#region Scaling Conveyors and Guards / ScaleConveyorLine
	/**
	 * Get the length of the conveyor line.
	 *
	 * If AutoScaleConveyors is enabled, this is the length required for the conveyor line, at its current angle, to span the assembly's x-axis one meter per unit of assembly x-scale.
	 *
	 * If AutoScaleConveyors is disabled, this is the sum of the lengths of all conveyors in the line.
	 * We assume that they're parallel and aligned end-to-end.
	 *
	 * @return The length of the conveyor line along its x-axis.
	 */
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

	/**
	 * Scale all conveyor children of a given node.
	 *
	 * This would be a great place to implement proportional scaling and positioning of the conveyors,
	 * but currently, we just scale every conveyor to the length of the whole line and leave its position alone.
	 *
	 * @param conveyorLine The parent of the conveyors.
	 * @param conveyorLineLength The length of the conveyor line to scale to. Ignored if AutoScaleConveyors is false.
	 */
	private void ScaleConveyorLine(Node3D conveyorLine, float conveyorLineLength) {
		foreach (Node child in conveyorLine.GetChildren()) {
			Node3D child3d = child as Node3D;
			if (IsConveyor(child3d)) {
				SetEditableInstance(child3d, true);
				ScaleConveyor(child3d, conveyorLineLength);
			}
		}
	}

	private static bool IsConveyor(Node node) {
		return node as IConveyor != null || node as RollerConveyor != null || node as CurvedRollerConveyor != null;
	}

	protected virtual void ScaleConveyor(Node3D conveyor, float conveyorLength) {
		if (AutoScaleConveyors) {
			conveyor.Scale = new Vector3(conveyorLength, 1f, this.Scale.Z);
		} else {
			// Always scale width.
			conveyor.Scale = new Vector3(conveyor.Scale.X, conveyor.Scale.Y, this.Scale.Z);
		}
	}
	#endregion Scaling Conveyors and Guards / ScaleConveyorLine

	#region Scaling Conveyors and Guards / ScaleSideGuardLine
	/**
	 * Scale all side guard children of a given node.
	 *
	 * This would be a great place to implement proportional scaling and positioning of the guards,
	 * but currently, we just scale every guard to the length of the whole line and leave its position alone.
	 *
	 * @param guardLine The parent of the side guards.
	 * @param conveyorLineLength The length of the conveyor line to scale to. Ignored if AutoScaleGuards is false.
	 */
	private void ScaleSideGuardLine(Node3D guardLine, float conveyorLineLength) {
		foreach (Node child in guardLine.GetChildren()) {
			Node3D child3d = child as Node3D;
			if (IsSideGuard(child3d)) {
				SetEditableInstance(child3d, true);
				ScaleSideGuard(child3d, conveyorLineLength);
			}
		}
	}

	private bool IsSideGuard(Node node) {
		return node as SideGuard != null || node as SideGuardCBC != null;
	}

	protected virtual void ScaleSideGuard(Node3D guard, float guardLength) {
		if (AutoScaleGuards) {
			guard.Scale = new Vector3(guardLength, 1f, 1f);
		}
	}
	#endregion Scaling Conveyors and Guards / ScaleSideGuardLine

	#region Scaling Conveyors and Guards / Update "LeftSide" and "RightSide" nodes
	private void UpdateSides()
	{
		UpdateSide(rightSide, true);
		UpdateSide(leftSide, false);
	}

	private void UpdateSide(Node3D side, bool isRight) {
		if (side == null || conveyors == null) {
			return;
		}
		LockSidePosition(side, isRight);
		var conveyorLineLength = GetConveyorLineLength();
		ScaleSideGuardLine(side, conveyorLineLength);
		// This would be a great place to implement proportional positioning of other Node3Ds attached to sides when the assembly scales.
	}

	private void LockSidePosition(Node3D side, bool isRight) {
		// Sides always snap onto the conveyor line
		side.Transform = conveyors.Transform;
		var offsetZ = (isRight? -1 : 1) * side.Basis.Z * (this.Scale.Z - 1f);
		side.Position += offsetZ;
	}
	#endregion Scaling Conveyors and Guards / Update "LeftSide" and "RightSide" nodes
	#endregion Scaling Conveyors and Guards

	#region Leg Stands
	#region Leg Stands / Conveyor coverage extents
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
				Vector3 conveyorExtent1 = localConveyorTransform.Orthonormalized() * new Vector3(-Mathf.Abs(localConveyorTransform.Basis.Scale.X * 0.5f) + AutoLegStandsMarginEnds, -AutoLegStandsModelGrabsOffset, 0f);
				Vector3 conveyorExtent2 = localConveyorTransform.Orthonormalized() * new Vector3(Mathf.Abs(localConveyorTransform.Basis.Scale.X * 0.5f) - AutoLegStandsMarginEnds, -AutoLegStandsModelGrabsOffset, 0f);
				// Update min and max.
				min = Mathf.Min(min, Mathf.Min(conveyorExtent2.X, conveyorExtent1.X));
				max = Mathf.Max(max, Mathf.Max(conveyorExtent2.X, conveyorExtent1.X));
			}
		}
		// Round to avoid floating point errors.
		return ((float) Math.Round(min, ROUNDING_DIGITS), (float) Math.Round(max, ROUNDING_DIGITS));
	}
	#endregion Leg Stands / Conveyor coverage extents

	#region Leg Stands / Update "LegStands" node
	private void UpdateLegStands()
	{
		if (legStands == null)
		{
			return;
		}

		LockLegStandsGroup();
		SyncLegStandsOffset();

		// If the leg stand scene changes, we need to regenerate everything.
		if (AutoLegStandsModelScene != autoLegStandsModelScenePrev) {
			DeleteSelfOwnedLegStands();
		}

		SnapAllLegStandsToPath();

		var autoLegStandsUpdateIsNeeded = AutoLegStandsIntervalLegsEnabled != autoLegStandsIntervalLegsEnabledPrev
			|| AutoLegStandsIntervalLegsInterval != autoLegStandsIntervalLegsIntervalPrev
			|| AutoLegStandsEndLegFront != autoLegStandsEndLegFrontPrev
			|| AutoLegStandsEndLegRear != autoLegStandsEndLegRearPrev
			|| AutoLegStandsMarginEndLegs != autoLegStandsMarginEndLegsPrev
			|| AutoLegStandsModelScene != autoLegStandsModelScenePrev
			|| legStandCoverageMin != legStandCoverageMinPrev
			|| legStandCoverageMax != legStandCoverageMaxPrev;
		if (autoLegStandsUpdateIsNeeded) {
			//GD.Print("Updating leg stands. Reason: ", AutoLegStandsUseInterval != autoLegStandsUseIntervalPrev
			//, AutoLegStandsInterval != autoLegStandsIntervalPrev
			//, AutoLegStandsFixedFrontLeg != autoLegStandsFixedFrontLegPrev
			//, AutoLegStandsFixedRearLeg != autoLegStandsFixedRearLegPrev
			//, AutoLegStandsFixedLegMargin != autoLegStandsFixedLegMarginPrev
			//, AutoLegStandsModelScene != autoLegStandsModelScenePrev
			//, legStandCoverageMin != legStandCoverageMinPrev
			//, legStandCoverageMax != legStandCoverageMaxPrev);
			AdjustAutoLegStandPositions();
			CreateAndRemoveAutoLegStands();
		}
		UpdateLegStandsHeightAndVisibility();
	}

	protected virtual void LockLegStandsGroup() {
		// Always align LegStands group with Conveyors group.
		if (conveyors != null) {
			legStands.Position = new Vector3(legStands.Position.X, legStands.Position.Y, conveyors.Position.Z);
			// Conveyors can't rotate anymore, so this doesn't do much.
			legStands.Rotation = new Vector3(0f, conveyors.Rotation.Y, 0f);
		}
	}

	/**
	 * Synchronize the offset of the leg stands with the assembly's AutoLegStandsIntervalLegsOffset property.
	 *
	 * If the property changes, the leg stands are moved to match.
	 * If the leg stands are moved manually, the property is updated.
	 * If both happen at the same time, the property wins.
	 *
	 * This currently shouldn't do anything for curved assemblies.
	 */
	private void SyncLegStandsOffset() {
		Basis assemblyScale = Basis.Identity.Scaled(this.Basis.Scale);
		Basis assemblyScalePrev = Basis.Identity.Scaled(transformPrev.Basis.Scale);
		Vector3 legStandsScaledPosition = assemblyScale * legStands.Position;
		Vector3 legStandsScaledPositionPrev = assemblyScalePrev * legStandsTransformPrev.Origin;
		if (AutoLegStandsIntervalLegsOffset != autoLegStandsIntervalLegsOffsetPrev) {
			Vector3 targetPosition = new Vector3(AutoLegStandsIntervalLegsOffset, legStandsScaledPosition.Y, legStandsScaledPosition.Z);
			legStands.Position = assemblyScale.Inverse() * targetPosition;
		} else {
			float offset = legStandsScaledPosition.X;
			float offsetPrev = legStandsScaledPositionPrev.X;
			float offsetDelta = Mathf.Abs(offset - offsetPrev);
			if (offsetDelta > 0.01f) {
				this.AutoLegStandsIntervalLegsOffset = offset;
				NotifyPropertyListChanged();
			}
		}
		autoLegStandsIntervalLegsOffsetPrev = AutoLegStandsIntervalLegsOffset;
		legStandsTransformPrev = legStands.Transform;
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
	#endregion Leg Stands / Update "LegStands" node

	#region Leg Stands / Basic constraints
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

	/**
	 * Snap a child leg stand to a position on the leg stands path.
	 *
	 * The leg stands path is a surface parallel to `legStands` Y axis.
	 * It represents any position that the conveyor line would be directly above or below at some length.
	 * For straight assemblies, this is `legStands` XY plane.
	 * For curved assemblies, this is overridden to be a cylinder centered on `legStands`.
	 *
	 * @param legStand The leg stand to reposition.
	 */
	private void SnapToLegStandsPath(Node3D legStand) {
		MoveLegStandToPathPosition(legStand, GetPositionOnLegStandsPath(legStand.Position));
	}

	/**
	 * Get the path position of a point projected onto the leg stands path.
	 *
	 * The path position is a linear representation of where a point is on the leg stands path.
	 * For straight assemblies, this is the X coordinate of the point.
	 * For curved assemblies, this is an angle of the point around the leg stands Y axis in degrees.
	 *
	 * @param position The point to project onto the leg stands path.
	 * @return The point's path position.
	 */
	protected virtual float GetPositionOnLegStandsPath(Vector3 position) {
		return position.X;
	}

	/**
	 * Move a leg stand to a given position on the leg stands path.
	 *
	 * The leg stand is moved and rotated to align with the path.
	 * The leg stand keeps its Y position and Z rotation.
	 * Curved assemblies override this and don't keep the Z rotation.
	 *
	 * @param legStand The leg stand to move.
	 * @param position The path position to move the leg stand to.
	 */
	protected virtual void MoveLegStandToPathPosition(Node3D legStand, float position) {
		legStand.Position = new Vector3(position, legStand.Position.Y, 0f);
		legStand.Rotation = new Vector3(0f, 0f, legStand.Rotation.Z);
	}
	#endregion Leg Stands / Basic constraints

	#region Leg Stands / Managing auto-instanced leg stands
	private void AdjustAutoLegStandPositions() {
		// Don't allow tiny or negative intervals.
		AutoLegStandsIntervalLegsInterval = Mathf.Max(0.5f, AutoLegStandsIntervalLegsInterval);
		if (AutoLegStandsIntervalLegsInterval == autoLegStandsIntervalLegsIntervalPrev && legStandCoverageMax == legStandCoverageMaxPrev && legStandCoverageMin == legStandCoverageMinPrev) {
			return;
		}
		foreach (Node child in legStands.GetChildren()) {
			ConveyorLeg legStand = child as ConveyorLeg;
			if (legStand == null) {
				continue;
			}
			// Only adjust leg stands that we created.
			if (legStand.Owner != GetTree().GetEditedSceneRoot()) { // FIXME this is too broad now
				continue;
			}
			// Handle front and rear legs first.
			if (AutoLegStandsEndLegFront && GetPositionOnLegStandsPath(legStand.Position) == legStandCoverageMinPrev) {
				MoveLegStandToPathPosition(legStand, legStandCoverageMin);
				continue;
			}
			if (AutoLegStandsEndLegRear && GetPositionOnLegStandsPath(legStand.Position) == legStandCoverageMaxPrev) {
				MoveLegStandToPathPosition(legStand, legStandCoverageMax);
				continue;
			}
			// Update leg stand position to the new interval.
			int legStandIndex = (int) Mathf.Round(GetPositionOnLegStandsPath(legStand.Position) / autoLegStandsIntervalLegsIntervalPrev);
			MoveLegStandToPathPosition(legStand, (float) Math.Round(legStandIndex * AutoLegStandsIntervalLegsInterval, ROUNDING_DIGITS));
		}

		autoLegStandsIntervalLegsIntervalPrev = AutoLegStandsIntervalLegsInterval;
	}

	private void CreateAndRemoveAutoLegStands() {
		// Don't allow negative margins.
		AutoLegStandsMarginEndLegs = Mathf.Max(0f, AutoLegStandsMarginEndLegs);
		// Enforce a margin from fixed front and rear legs if they exist.
		float frontMargin = AutoLegStandsEndLegFront ? AutoLegStandsMarginEndLegs : 0f;
		float rearMargin = AutoLegStandsEndLegRear ? AutoLegStandsMarginEndLegs : 0f;
		float firstPosition = (float) Math.Ceiling((legStandCoverageMin + frontMargin) / AutoLegStandsIntervalLegsInterval) * AutoLegStandsIntervalLegsInterval;
		float lastPosition = (float) Math.Floor((legStandCoverageMax - rearMargin) / AutoLegStandsIntervalLegsInterval) * AutoLegStandsIntervalLegsInterval;
		int intervalLegStandCount;
		if (firstPosition > lastPosition) {
			// Invalid range implies zero interval-aligned leg stands are needed.
			intervalLegStandCount = 0;
		} else {
			intervalLegStandCount = (int) ((lastPosition - firstPosition) / AutoLegStandsIntervalLegsInterval) + 1;
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
			var isFrontLeg = AutoLegStandsEndLegFront && GetPositionOnLegStandsPath(legStand.Position) == legStandCoverageMin;
			var isRearLeg = AutoLegStandsEndLegRear && GetPositionOnLegStandsPath(legStand.Position) == legStandCoverageMax;
			if (isFrontLeg || isRearLeg) {
				hasFrontLeg = hasFrontLeg || isFrontLeg;
				hasRearLeg = hasRearLeg || isRearLeg;
				continue;
			}
			if (AutoLegStandsIntervalLegsEnabled) {
				// Delete leg stands that are not in the new interval.
				if (GetPositionOnLegStandsPath(legStand.Position) < firstPosition || GetPositionOnLegStandsPath(legStand.Position) > lastPosition) {
					legStand.QueueFree();
				} else {
					// Store leg stands that are in the new interval.
					legStandsInventory[(int) Math.Round((GetPositionOnLegStandsPath(legStand.Position) - firstPosition) / AutoLegStandsIntervalLegsInterval)] = legStand;
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
		if (!hasFrontLeg && AutoLegStandsEndLegFront) {
			AddLegStandInstance(legStandCoverageMin);
		}
		if (!hasRearLeg && AutoLegStandsEndLegRear) {
			AddLegStandInstance(legStandCoverageMax);
		}
		if (!AutoLegStandsIntervalLegsEnabled) {
			return;
		}
		for (int i = 0; i < intervalLegStandCount; i++) {
			if (legStandsInventory[i] == null) {
				AddLegStandInstance((float) Math.Round(firstPosition + i * AutoLegStandsIntervalLegsInterval, ROUNDING_DIGITS));
			}
		}
	}

	private Node3D AddLegStandInstance(float position) {
		ConveyorLeg legStand = AutoLegStandsModelScene.Instantiate() as ConveyorLeg;
		MoveLegStandToPathPosition(legStand, position);
		legStands.AddChild(legStand, forceReadableName: true);
		legStand.Owner = GetTree().GetEditedSceneRoot();
		return legStand;
	}
	#endregion Leg Stands / Managing auto-instanced leg stands

	#region Leg Stands / Auto-height and visibility
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
	#endregion Leg Stands / Auto-height and visibility
	#endregion Leg Stands
}
