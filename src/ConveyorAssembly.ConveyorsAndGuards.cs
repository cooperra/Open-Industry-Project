using Godot;
using System;

public partial class ConveyorAssembly : Node3D
{
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
}
