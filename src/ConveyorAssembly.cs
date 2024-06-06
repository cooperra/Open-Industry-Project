using Godot;
using System;

[Tool]
public partial class ConveyorAssembly : Node3D
{
	Node3D conveyors;
	Node3D rightSide;
	Node3D leftSide;
	Node3D legStands;
	Transform3D previousTransform;

	[ExportGroup("Conveyor Automation", "Auto")]
	[Export]
	public bool AutoScaleConveyors { get; set; } = true;
	[Export]
	public bool AutoScaleGuards { get; set; } = true;

	[ExportGroup("Leg Stands Automation", "AutoLegStands")]
	[Export]
	public bool AutoLegStandsEnabled { get; set; } = true;

	[Export(PropertyHint.None, "suffix:m")]
	public float AutoLegStandsInterval { get; set; } = 1f;
	private float previousAutoLegStandsInterval;

	[Export]
	public PackedScene AutoLegStandsScene = GD.Load<PackedScene>("res://parts/ConveyorLegBC.tscn");

	private float legStandCoverageMin;
	private float legStandCoverageMax;

	public override void _Ready()
	{
		conveyors = GetNode<Node3D>("Conveyors");
		rightSide = GetNode<Node3D>("RightSide");
		leftSide = GetNode<Node3D>("LeftSide");
		legStands = GetNode<Node3D>("LegStands");
		previousTransform = this.Transform;
		previousAutoLegStandsInterval = AutoLegStandsInterval;
		// All existing leg stands that we own must be regenerated. We can't trust them remain correct.
		DeleteSelfOwnedLegStands();
	}

	public override void _PhysicsProcess(double delta)
	{
		PreventAllChildScaling();
		UpdateConveyors();
		UpdateSides();
		UpdateLegStandCoverage();
		UpdateLegStands();
		previousTransform = this.Transform;
	}

	private void UpdateConveyors()
	{
		if (conveyors == null)
		{
			return;
		}

		// Lock Z position
		conveyors.Position = new Vector3(conveyors.Position.X, conveyors.Position.Y, 0f);
		// Lock X and Y rotation
		if (conveyors.Rotation.X > 0.001f || conveyors.Rotation.X < -0.001f || conveyors.Rotation.Y > 0.001f || conveyors.Rotation.Y < -0.001) {
			// This seems to mess up scale, but at least that's fixed on the next frame.
			conveyors.Rotation = new Vector3(0f, 0f, conveyors.Rotation.Z);
		}
		var conveyorLineLength = GetConveyorLineLength();
		// TODO enact a scheme where conveyors are scaled proportionally to the each other. (But there is that factor where the tips will overlap... Better than nothing!)

		foreach (Node child in conveyors.GetChildren()) {
			Node3D conveyor = child as Node3D;
			if (conveyor != null && conveyor as IConveyor != null) {
				if (AutoScaleConveyors) {
					conveyor.Scale = new Vector3(conveyorLineLength, 1f, this.Scale.Z);
				} else {
					// Always scale width.
					conveyor.Scale = new Vector3(conveyor.Scale.X, conveyor.Scale.Y, this.Scale.Z);
				}
			}
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
			if (guard as SideGuard != null) {
				guard.Scale = new Vector3(conveyorLineLength, 1f, 1f);
			}
		}
	}

	private void UpdateLegStands()
	{
		if (legStands == null)
		{
			return;
		}

		// Always align LegStands group with Conveyors group.
		if (conveyors != null) {
			legStands.Position = new Vector3(legStands.Position.X, legStands.Position.Y, conveyors.Position.Z);
			// TODO rotation is funky now
			legStands.Rotation = new Vector3(0f, conveyors.Rotation.Y, 0f);
		}

		// Force legStand alignment with LegStands group.
		foreach (Node child in legStands.GetChildren()) {
			ConveyorLeg legStand = child as ConveyorLeg;
			if (legStand == null) {
				continue;
			}
			legStand.Position = new Vector3(legStand.Position.X, legStand.Position.Y, 0f);
			legStand.Rotation = new Vector3(0f, 0f, legStand.Rotation.Z);
			legStand.Scale = new Vector3(1f, legStand.Scale.Y, this.Scale.Z);
		}

		if (AutoLegStandsEnabled) {
			DeleteUnalignedAutoLegStands();
			HandleLegStandsIntervalChange();
			CreateAndRemoveAutomaticLegStands();
		}
		UpdateLegStandsHeightAndVisibility();
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
				legStand.QueueFree();
			}
		}
	}

	private void DeleteUnalignedAutoLegStands() {
		// Delete auto-placed leg stands that are not aligned to the previous interval.
		foreach (Node child in legStands.GetChildren()) {
			ConveyorLeg legStand = child as ConveyorLeg;
			if (legStand == null) {
				continue;
			}
			// Only delete leg stands that we created.
			if (legStand.Owner != this) {
				continue;
			}
			if (legStand.Position.X % previousAutoLegStandsInterval != 0f) {
				legStand.QueueFree();
			}
		}
	}

	private void HandleLegStandsIntervalChange() {
		if (AutoLegStandsInterval == previousAutoLegStandsInterval) {
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
			// Update leg stand position to the new interval.
			var legStandIndex = legStand.Position.X / previousAutoLegStandsInterval;
			legStand.Position = new Vector3(legStandIndex * AutoLegStandsInterval, legStand.Position.Y, 0f);
		}

		previousAutoLegStandsInterval = AutoLegStandsInterval;
	}

	private void CreateAndRemoveAutomaticLegStands() {
		float firstPosition = (float) Math.Ceiling(legStandCoverageMin / AutoLegStandsInterval) * AutoLegStandsInterval;
		float lastPosition = (float) Math.Floor(legStandCoverageMax / AutoLegStandsInterval) * AutoLegStandsInterval;
		int legStandCount = (int) ((lastPosition - firstPosition) / AutoLegStandsInterval) + 1;
		ConveyorLeg[] legStandsArray = new ConveyorLeg[legStandCount];

		// Inventory our existing leg stands and delete the ones we don't need.
		foreach (Node child in legStands.GetChildren()) {
			ConveyorLeg legStand = child as ConveyorLeg;
			if (legStand == null) {
				continue;
			}
			// Only manage leg stands that we created.
			if (legStand.Owner != this) {
				continue;
			}
			// Delete leg stands that are not in the new interval.
			if (legStand.Position.X < firstPosition || legStand.Position.X > lastPosition) {
				legStand.QueueFree();
			} else {
				// Store leg stands that are in the new interval.
				legStandsArray[(int) ((legStand.Position.X - firstPosition) / AutoLegStandsInterval)] = legStand;
			}
		}

		// Create the missing leg stands.
		for (int i = 0; i < legStandCount; i++) {
			if (legStandsArray[i] == null) {
				ConveyorLeg legStand = AutoLegStandsScene.Instantiate() as ConveyorLeg;
				legStand.Position = new Vector3(firstPosition + i * AutoLegStandsInterval, 0f, 0f);
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
		var hingeOffset = 0.372f;
		var conveyorPlane = new Plane(Vector3.Up, new Vector3(0f, -hingeOffset, 0f)) * conveyors.Transform.AffineInverse() * legStands.Transform;

		foreach (Node child in legStands.GetChildren()) {
			ConveyorLeg legStand = child as ConveyorLeg;
			if (legStand == null) {
				continue;
			}
			// Raycast from the minimum-height tip of the leg stand to the conveyor plane.
			var intersection = conveyorPlane.IntersectsRay(legStand.Position + legStand.Basis.Y.Normalized(), legStand.Basis.Y.Normalized());
			if (intersection == null) {
				legStand.Visible = false;
				// Set scale to minimum height.
				legStand.Scale = new Vector3(1f, 1f, legStand.Scale.Z);
				continue;
			}
			float legHeight = ((Vector3) intersection).Y;
			legStand.Scale = new Vector3(1f, legHeight, legStand.Scale.Z);
			legStand.GrabsRotation = Mathf.RadToDeg(legStand.Basis.Y.SignedAngleTo(conveyorPlane.Normal, legStand.Basis.Z));
			// Only show leg stands that touch a conveyor.
			var tipPosition = legStand.Position + legStand.Basis.Y;
			legStand.Visible = legStandCoverageMin <= tipPosition.X && tipPosition.X <= legStandCoverageMax;
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

		var basisRotationPrev = previousTransform.Basis.Orthonormalized();
		var basisScalePrev = basisRotationPrev.Inverse() * previousTransform.Basis;
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


	private void PreventAllChildScaling() {
		foreach (Node3D child in GetChildren()) {
			Node3D child3D = child as Node3D;
			if (child3D != null) {
				PreventChildScaling(child3D);
			}
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
			if (conveyor != null && conveyor as IConveyor != null) {
				// Assume conveyor scale == length.
				sum += conveyor.Scale.X;
			}
		}
		return sum;
	}

	private void UpdateLegStandCoverage() {
		if (legStands == null || conveyors == null) {
			(legStandCoverageMin, legStandCoverageMax) = (0f, 0f);
			return;
		}
		var min = float.MaxValue;
		var max = float.MinValue;
		foreach (Node child in conveyors.GetChildren()) {
			Node3D conveyor = child as Node3D;
			if (conveyor != null && conveyor as IConveyor != null) {
				// Get the conveyor's Transform in the legStands space.
				Transform3D localConveyorTransform = legStands.Transform.AffineInverse() * conveyors.Transform * conveyor.Transform;
				// Get the X extents of the conveyor in the legStands space.
				// Assume X scale is equal to length, so at scale 1, half the length is 0.5.
				Vector3 conveyorExtent1 = localConveyorTransform * new Vector3(0.5f, 0f, 0f);
				Vector3 conveyorExtent2 = localConveyorTransform * new Vector3(-0.5f, 0f, 0f);
				// Update min and max.
				min = Mathf.Min(min, Mathf.Min(conveyorExtent2.X, conveyorExtent1.X));
				max = Mathf.Max(max, Mathf.Max(conveyorExtent2.X, conveyorExtent1.X));
			}
		}
		(legStandCoverageMin, legStandCoverageMax) = (min, max);
	}
}
